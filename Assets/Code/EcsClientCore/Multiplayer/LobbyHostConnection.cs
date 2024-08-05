using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlatBuffers;
using Messages;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Networking.Transport.Utilities;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.Assertions;

namespace Indigo.EcsClientCore
{
  public class PlayerLobbyInfo
  {
    public int    slot;
    public string name;
  }

  public interface ILobbyHostConnection
  {
     List<PlayerLobbyInfo> PlayerInfos { get; }
     List<Region>          Regions     { get; }
     string                JoinCode    { get; }
     string                RegionId    { get; }
     Task                  Init();
     void                  SendLoadGameMessage(string code);
     void                  SendGameStartToAll();
     void                  SendJoinGameAck(int contentTypedSlot);
     Task                  HostLobby(string    regionId);
     void                  Update();
     void                  Dispose();

     event Action<PlayerLobbyInfo> OnClientJoinedLobby;
  }

  public class LobbyHostConnection : RelayConnectionBase, ILobbyHostConnection
  {
    private const int MAX_CONNECTIONS = 3;

    public string RegionId { get; set; }
    public string JoinCode { get; set; }

    public string HostScreenName;


    private int _nextOpenSlot = 1;
    Allocation  hostAllocation;

    private Dictionary<int, int>  _playerSlotToConnectionId = new Dictionary<int, int>();
    private Dictionary<int, int>  connectionIdToIdx         = new Dictionary<int, int>();
    NativeList<NetworkConnection> serverConnections;

    public List<PlayerLobbyInfo> PlayerInfos { get; } = new List<PlayerLobbyInfo>();
    public List<Region>          Regions     { get; } = new List<Region>();

    public event Action<PlayerLobbyInfo> OnClientJoinedLobby;

    public LobbyHostConnection(string hostScreenName, ClientLogger logger) : base(logger)
    {
      HostScreenName = hostScreenName;
      PlayerInfos.Add(new PlayerLobbyInfo(){ name = hostScreenName, slot = 0});
    }

    public override void Dispose()
    {
      base.Dispose();
      serverConnections.Dispose();
    }

    private async Task Allocate(string regionId)
    {
      RegionId = regionId;
      hostAllocation = await RelayService.Instance.CreateAllocationAsync(MAX_CONNECTIONS, regionId);
      serverConnections = new NativeList<NetworkConnection>(MAX_CONNECTIONS, Allocator.Persistent);
    }
    
    private void BindHost()
    {
      var relayServerData = new RelayServerData(hostAllocation, "udp");
      
      var settings = new NetworkSettings();
      settings.WithRelayParameters(ref relayServerData);
      settings.WithReliableStageParameters();
      
      _networkDriver = NetworkDriver.Create(settings);
      _networkDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage));

      if (_networkDriver.Bind(NetworkEndPoint.AnyIpv4) != 0)
      {
        Debug.LogError("Host client failed to bind");
      }
      else
      {
        if (_networkDriver.Listen() != 0)
        {
          Debug.LogError("Host client failed to listen");
        }
        else
        {
          Debug.Log("Host client bound to Relay server");
        }
      }
    }
    
    private async Task PopulateRegions()
    {
      var allRegions = await RelayService.Instance.ListRegionsAsync();
      Regions.Clear();
      foreach (var region in allRegions)
      {
        Regions.Add(region);
      }
    }
    
    private async Task GetJoinCode()
    {
      try
      {
        JoinCode = await RelayService.Instance.GetJoinCodeAsync(hostAllocation.AllocationId);
      }
      catch (RelayServiceException ex)
      {
        Debug.LogError(ex.Message + "\n" + ex.StackTrace);
      }
    }

    public async Task Init()
    {
      await PopulateRegions();
    }

    public void SendJoinGameAck(int playerSlot)
    {
      _fbb.Clear();
      var finalMsg = Messages.LobbyMessage.CreateLobbyMessage(_fbb, LobbyMessageType.JoinGameAck);
      _fbb.Finish(finalMsg.Value);

      var cId = _playerSlotToConnectionId[playerSlot];
      SendMessageData(_fbb.DataBuffer.ToSizedArray(), serverConnections[connectionIdToIdx[cId]]);
    }

    public async Task HostLobby(string regionId)
    {
      await Allocate(regionId);
      BindHost();
      await GetJoinCode();
    }

    public void Update()
    {
      UpdateHost();
    }

    private void UpdateHost()
    {
        // Skip update logic if the Host is not yet bound.
        if (!_networkDriver.IsCreated || !_networkDriver.Bound)
        {
            return;
        }

        // This keeps the binding to the Relay server alive,
        // preventing it from timing out due to inactivity.
        _networkDriver.ScheduleUpdate().Complete();

        // Clean up stale connections.
        for (int i = 0; i < serverConnections.Length; i++)
        {
          if (!serverConnections[i].IsCreated)
          {
              Debug.Log("Stale connection removed");
              serverConnections.RemoveAt(i);
              --i;
          }
        }
        
        NetworkConnection incomingConnection;
        while ((incomingConnection = _networkDriver.Accept()) != default(NetworkConnection))
        {
          serverConnections.Add(incomingConnection);
            connectionIdToIdx.Add(incomingConnection.InternalId, serverConnections.Length - 1);
        }
        
        for (int i = 0; i < serverConnections.Length; i++)
        {
            Assert.IsTrue(serverConnections[i].IsCreated);
            ProcessAllReceivedMessageData(serverConnections[i], MessageHandler);
        }
    }
    
    private void SendJoinResponse(bool success, int slot, NetworkConnection networkConnection)
    {
      _fbb.Clear();
      
      var content = Messages.JoinLobbyResponseMsgContent.CreateJoinLobbyResponseMsgContent(_fbb, success, slot);
      _fbb.Finish(content.Value);
      var msg = _fbb.DataBuffer.ToSizedArray();

      _fbb.Clear();
      var msgContentOffset =  Messages.LobbyMessage.CreateContentVector(_fbb, msg);
      var finalMsg = Messages.LobbyMessage.CreateLobbyMessage(_fbb, LobbyMessageType.JoinLobbyResponse, msgContentOffset);
      _fbb.Finish(finalMsg.Value);

      SendMessageData(_fbb.DataBuffer.ToSizedArray(), networkConnection);
    }
    
    public void SendPlayerJoinedMessageToAll()
    {
      _fbb.Clear();

      List<Offset<PlayerInfo>> playerInfoOffsets = new List<Offset<PlayerInfo>>();
      foreach (var playerInfo in PlayerInfos)
      {
        playerInfoOffsets.Add(Messages.PlayerInfo.CreatePlayerInfo(_fbb, _fbb.CreateString(playerInfo.name), playerInfo.slot));
      }

      var playersVector = Messages.PlayerJoinedLobbyMsgContent.CreatePlayersVector(_fbb, playerInfoOffsets.ToArray());
      var content = Messages.PlayerJoinedLobbyMsgContent.CreatePlayerJoinedLobbyMsgContent(_fbb, playersVector);
      _fbb.Finish(content.Value);
      var msg = _fbb.DataBuffer.ToSizedArray();

      _fbb.Clear();
      var msgContentOffset =  Messages.LobbyMessage.CreateContentVector(_fbb, msg);
      var finalMsg = Messages.LobbyMessage.CreateLobbyMessage(_fbb, LobbyMessageType.PlayerJoinedLobby, msgContentOffset);
      _fbb.Finish(finalMsg.Value);

      foreach (var curConnection in serverConnections)
      {
        SendMessageData(_fbb.DataBuffer.ToSizedArray(), curConnection); 
      }
    }
    
    public void SendGameStartToAll()
    {
      _fbb.Clear();
      var finalMsg = Messages.LobbyMessage.CreateLobbyMessage(_fbb, LobbyMessageType.StartGame);
      _fbb.Finish(finalMsg.Value);

      foreach (var curConnection in serverConnections)
      {
        SendMessageData(_fbb.DataBuffer.ToSizedArray(), curConnection); 
      }
    }

    private void MessageHandler(NetworkConnection connection, NetworkEvent.Type eventType, byte[] contentBuffer, int contentLength)
    {
      switch (eventType)
      {
        case NetworkEvent.Type.Data:

          int offset = contentBuffer.Length - contentLength;
          var lobbyMessage = Messages.LobbyMessage.GetRootAsLobbyMessage(new ByteBuffer(contentBuffer, offset));
          if (lobbyMessage.Type == LobbyMessageType.JoinLobby)
          {
            var contentTyped =
              Messages.JoinLobbyMsgContent.GetRootAsJoinLobbyMsgContent(new ByteBuffer(lobbyMessage
                                                                                         .GetContentArray()));

            int slot = -1;
            bool containsPlayerName = PlayerInfos.Exists(info => info.name == contentTyped.PlayerName);
            if (!containsPlayerName)
            {
              slot = _nextOpenSlot++;
              var newPlayer = new PlayerLobbyInfo() {name = contentTyped.PlayerName, slot = slot};
              PlayerInfos.Add(newPlayer); 
              OnClientJoinedLobby?.Invoke(newPlayer);
              
              _playerSlotToConnectionId.Add(newPlayer.slot, connection.InternalId);
            }
            
            bool success = !containsPlayerName;
            SendJoinResponse(success, slot, connection);

            SendPlayerJoinedMessageToAll();
          }

          //hostLatestMessageReceived = msg.ToString();
          break;

        // Handle Disconnect events.
        case NetworkEvent.Type.Disconnect:
          Debug.Log("Server received disconnect from client");

          int idx = connectionIdToIdx[connection.InternalId];
          serverConnections[idx] = default(NetworkConnection);
          break;
      }
    }

    public void SendLoadGameMessage(string code)
    {
      _fbb.Clear();
      
      var content = Messages.LoadGameMsgContent.CreateLoadGameMsgContent(_fbb, _fbb.CreateString(code));
      _fbb.Finish(content.Value);
      var msg = _fbb.DataBuffer.ToSizedArray();

      _fbb.Clear();
      var msgContentOffset =  Messages.LobbyMessage.CreateContentVector(_fbb, msg);
      var finalMsg = Messages.LobbyMessage.CreateLobbyMessage(_fbb, LobbyMessageType.LoadGame, msgContentOffset);
      _fbb.Finish(finalMsg.Value);

      foreach (var curConnection in serverConnections)
      {
        SendMessageData(_fbb.DataBuffer.ToSizedArray(), curConnection); 
      }
    }
  } 
}
