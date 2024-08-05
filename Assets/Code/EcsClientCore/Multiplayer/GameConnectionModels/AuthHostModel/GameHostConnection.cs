using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlatBuffers;
using Messages;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace Indigo.EcsClientCore
{
  public struct SerializedAuthInputData
  {
    public byte[]         serializedData;
    public int            serializedDataLength;
  }

  public interface IGameHostConnection
  {
    void Update();
    void SetHostReady();
    Task<string> HostRelayServer(string regionId);
    void SendAllNonAckedAuthInput(int playerId, List<SerializedAuthInputData> tempAckDataList, int startFrame, int latestFrameReceivedFromPlayer, int msAhead);
    event Action<PlayerInputContent> OnPlayerInputReceived;
  }

  public class GameHostConnection : RelayConnectionBase, IGameHostConnection
  {
    private const int    MAX_CONNECTIONS = 3;
    
    private ILobbyHostConnection _lobbyHostConnection;
    
    public string JoinCode;
    
    private Allocation                    hostAllocation;
    private Dictionary<int, int>          connectionIdToIdx = new Dictionary<int, int>();
    private NativeList<NetworkConnection> serverConnections;

    private List<Offset<ByteArray>> _byteArrayOffets  = new List<Offset<ByteArray>>();

    private Dictionary<NetworkConnection, int> _connectionToPlayerSlot = new Dictionary<NetworkConnection, int>();
    private Dictionary<int, NetworkConnection> _playerSlotToConnection = new Dictionary<int, NetworkConnection>();

    private List<int> _readyPlayerSlots = new List<int>();
    
    private bool _sentGameStartEvent;
    
    public event Action                              OnGameStartSent;
    public event Action<string>                      OnClientJoined;
    public event Action<Messages.PlayerInputContent> OnPlayerInputReceived;

    public GameHostConnection(ILobbyHostConnection lobbyHostConnection, ClientLogger logger) : base(logger)
    {
      _lobbyHostConnection = lobbyHostConnection;
    }

    public void SetHostReady()
    {
      _readyPlayerSlots.Add(0);
    }
    
    public override void Dispose()
    {
      base.Dispose();
      serverConnections.Dispose();
    }
    
    public async Task Allocate(string regionId)
    {
      hostAllocation = await RelayService.Instance.CreateAllocationAsync(MAX_CONNECTIONS, regionId);
      serverConnections = new NativeList<NetworkConnection>(MAX_CONNECTIONS, Allocator.Persistent);
    }
      
    public void BindHost()
    {
      var relayServerData = new RelayServerData(hostAllocation, "udp");
      
      var settings = new NetworkSettings();
      settings.WithRelayParameters(ref relayServerData);
      _networkDriver = NetworkDriver.Create(settings);

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
    
    public async Task<string> HostRelayServer(string regionId)
    {
      await Allocate(regionId);
      await GetJoinCode();
      BindHost();
      return JoinCode;
    }
    
    public void Update()
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
        ProcessAllReceivedMessageData(serverConnections[i], MessageHandler);
      }
    }

    private void MessageHandler(NetworkConnection connection, NetworkEvent.Type eventType, byte[] contentBuffer, int contentLength)
    {
      switch (eventType)
      {
        // Handle Relay events.
        case NetworkEvent.Type.Data:

          int offset = contentBuffer.Length - contentLength;
          var msg = Messages.GameMessage.GetRootAsGameMessage(new ByteBuffer(contentBuffer, offset));

          if (msg.Type == GameMessageType.JoinGame)
          {
            var contentTyped =
              Messages.JoinGameMsgContent.GetRootAsJoinGameMsgContent(new ByteBuffer(msg.GetContentArray()));

            _readyPlayerSlots.Add(contentTyped.Slot);
            _connectionToPlayerSlot[connection] = contentTyped.Slot;
            _playerSlotToConnection[contentTyped.Slot] = connection;
            
            _lobbyHostConnection.SendJoinGameAck(contentTyped.Slot);

            if (!_sentGameStartEvent && _readyPlayerSlots.Count >= _lobbyHostConnection.PlayerInfos.Count)
            {
              _sentGameStartEvent = true;
              _lobbyHostConnection.SendGameStartToAll();
              OnGameStartSent?.Invoke();
            }
          }
          else if (msg.Type == GameMessageType.PlayerInput)
          {
            var contentTyped =
              Messages.PlayerInputContent.GetRootAsPlayerInputContent(new ByteBuffer(msg.GetContentArray()));
            
            Debug.Log($"Received player input {contentTyped.InputsLength} ss");
            OnPlayerInputReceived?.Invoke(contentTyped);
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

    public void SendAllNonAckedAuthInput(int playerId, List<SerializedAuthInputData> tempAckDataList, int startFrame, int latestFrameReceivedFromPlayer, int msAhead)
    {
      _fbb.Clear();

      _byteArrayOffets.Clear();
      foreach (var ackData in tempAckDataList)
      {
        VectorOffset byteVectorOffset = FlatBufferUtil.AddVectorToBufferFromByteArray(_fbb, ackData.serializedData, ackData.serializedDataLength);
        _byteArrayOffets.Add(Messages.ByteArray.CreateByteArray(_fbb, byteVectorOffset));
      }
        
      VectorOffset framesOffset = FlatBufferUtil.AddVectorToBufferFromOffsetList(_fbb, Messages.AuthInputContent.StartFramesVector, _byteArrayOffets);
        
      var content = Messages.AuthInputContent.CreateAuthInputContent(_fbb, latestFrameReceivedFromPlayer, msAhead, startFrame, framesOffset);
      _fbb.Finish(content.Value);
      var msg = _fbb.DataBuffer.ToSizedArray();

      _fbb.Clear();
      var msgContentOffset = Messages.GameMessage.CreateContentVector(_fbb, msg);
      var finalMsg = ConnectionUtil.CreateGameMsg(_fbb, GameMessageType.AuthInput, msgContentOffset);
      _fbb.Finish(finalMsg.Value);

      NetworkConnection playerConnection = _playerSlotToConnection[playerId];
      SendMessageData(_fbb.DataBuffer.ToSizedArray(), playerConnection);
    }
  } 
}
