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
  public class P2PGameHostConnection : P2PGameConnectionBase, IP2PGameHostConnection
  {
    private const int MAX_CONNECTIONS = 1;
    
    private ILobbyHostConnection _lobbyHostConnection;
    
    public string JoinCode;
    
    private Allocation                    hostAllocation;
    private int _connectionId;
    private NetworkConnection _hostConnection;

    private List<int> _readyPlayerSlots = new List<int>();
    
    private bool _sentGameStartEvent;
    
    public event Action                                OnGameStartSent;
    public event Action<string>                        OnClientJoined;
    public event Action<Messages.P2PPlayerSyncContent, string, long> OnPeerSyncReceived;

    public P2PGameHostConnection(ILobbyHostConnection lobbyHostConnection, ClientLogger logger) : base(logger)
    {
      _lobbyHostConnection = lobbyHostConnection;
    }

    public void SetHostReady()
    {
      _readyPlayerSlots.Add(0);
    }
    
    public async Task Allocate(string regionId)
    {
      hostAllocation = await RelayService.Instance.CreateAllocationAsync(MAX_CONNECTIONS, regionId);
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
        
      NetworkConnection incomingConnection;
      while ((incomingConnection = _networkDriver.Accept()) != default(NetworkConnection))
      {
        _hostConnection = incomingConnection;
      }
        
      if(_hostConnection.IsCreated)
      {
        //Debug.Log("processing host msg");
        ProcessAllReceivedMessageData(_hostConnection, MessageHandler);
      }
    }

    private void MessageHandler(NetworkConnection connection, NetworkEvent.Type eventType, byte[] contentBuffer, int contentLength)
    {
      switch (eventType)
      {
        // Handle Relay events.
        case NetworkEvent.Type.Data:

          //Debug.Log("got host data msg");
          int offset = contentBuffer.Length - contentLength;
          var msg = Messages.GameMessage.GetRootAsGameMessage(new ByteBuffer(contentBuffer, offset));

          if (msg.Type == GameMessageType.JoinGame)
          {
            var contentTyped =
              Messages.JoinGameMsgContent.GetRootAsJoinGameMsgContent(new ByteBuffer(msg.GetContentArray()));

            _readyPlayerSlots.Add(contentTyped.Slot);

            _lobbyHostConnection.SendJoinGameAck(contentTyped.Slot);

            if (!_sentGameStartEvent && _readyPlayerSlots.Count >= _lobbyHostConnection.PlayerInfos.Count)
            {
              _sentGameStartEvent = true;
              _lobbyHostConnection.SendGameStartToAll();
              OnGameStartSent?.Invoke();
            }
          }
          else if (msg.Type == GameMessageType.P2PSyncContent)
          {
            var contentTyped =
              Messages.P2PPlayerSyncContent.GetRootAsP2PPlayerSyncContent(new ByteBuffer(msg.GetContentArray()));
            
            //Debug.Log($"Received peer input {contentTyped.InputsLength} ss");
            OnPeerSyncReceived?.Invoke(contentTyped, msg.MsgId, msg.MsgCreationTime);
          }
                        
          //hostLatestMessageReceived = msg.ToString();
          break;

        // Handle Disconnect events.
        case NetworkEvent.Type.Disconnect:
          Debug.Log("Server received disconnect from client");

          _hostConnection = default;
          break;
      }
    }

    public override int GetPlayerSlot()
    {
      return 0;
    }

    public override NetworkConnection GetPeerConnection()
    {
      return _hostConnection;
    }
  } 
}
