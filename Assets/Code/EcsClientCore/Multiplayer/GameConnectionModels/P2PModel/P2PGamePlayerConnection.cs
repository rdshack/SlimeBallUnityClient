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
  public interface IP2PGameConnection
  {
    int GetPlayerSlot();
    void Update();

    public void SendPeerSyncData(List<SerializedPlayerFrameInputData> frameInputs,
                                 int                                  latestAckForPeer,
                                 int                                  firstFrame,
                                 List<int>                            hashes,
                                 int                                  hashStart,
                                 int                                  latestHashAckForPeer);
    
    event Action<P2PPlayerSyncContent, string, long> OnPeerSyncReceived;
  }
  
  public class P2PGamePlayerConnection : P2PGameConnectionBase, IP2PGameConnection
  {
    private ILobbyClientConnection _lobbyClientConnection;
    
    private JoinAllocation         _playerAllocation;
    private NetworkConnection      _clientConnection;
    
    private bool _joinAcked;
    
    private Allocation                    hostAllocation;

    public event Action<Messages.PlayerInputContent>   OnPlayerInputReceived;
    public event Action<Messages.P2PPlayerSyncContent, string, long> OnPeerSyncReceived;

    public P2PGamePlayerConnection(ILobbyClientConnection lobbyClientConnection, ClientLogger logger) : base(logger)
    {
      _lobbyClientConnection = lobbyClientConnection;
      _lobbyClientConnection.OnJoinAcked += JoinAcked;
    }
    
    private void JoinAcked()
    {
      _joinAcked = true;
    }
    
    public void Update()
    {
      // Skip update logic if the Player is not yet bound.
      if (!_networkDriver.IsCreated || !_networkDriver.Bound)
      {
        return;
      }
          
      //Debug.Log(_networkDriver.GetConnectionState(clientConnection));

      // This keeps the binding to the Relay server alive,
      // preventing it from timing out due to inactivity.
      _networkDriver.ScheduleUpdate().Complete();

      if (_clientConnection.IsCreated)
      {
        ProcessAllReceivedMessageData(_clientConnection, MessageHandler); 
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
          

          if (msg.Type == GameMessageType.P2PSyncContent)
          {
            var contentTyped =
              Messages.P2PPlayerSyncContent.GetRootAsP2PPlayerSyncContent(new ByteBuffer(msg.GetContentArray()));
            
            OnPeerSyncReceived?.Invoke(contentTyped, msg.MsgId, msg.MsgCreationTime);
          }
                        
          //hostLatestMessageReceived = msg.ToString();
          break;

        // Handle Disconnect events.
        case NetworkEvent.Type.Disconnect:
          Debug.Log("Server received disconnect from client");
          _clientConnection = default(NetworkConnection);
          break;
      }
    }
    
    public async Task<bool> JoinGame(string joinCode)
    {
      try
      {
        _playerAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
        Debug.Log("Player Allocation ID: " + _playerAllocation.AllocationId);
      }
      catch (RelayServiceException ex)
      {
        Debug.LogError(ex.Message + "\n" + ex.StackTrace);
      }

      BindPlayer();
      ConnectPlayer();

      Task timeout = Task.Delay(5000);
      while (!timeout.IsCompleted)
      {
        await Task.Delay(100);

        if (_networkDriver.GetConnectionState(_clientConnection) == NetworkConnection.State.Connected)
        {
          return true;
        }
      }

      return false;
    }
    
    private void ConnectPlayer()
    {
      // Sends a connection request to the Host Player.
      _clientConnection = _networkDriver.Connect();
      Debug.Log($"Player - Connecting to Host's client result: {_clientConnection!=default}");
    }
    
    private void BindPlayer()
    {
      Debug.Log("Player - Binding to the Relay server using UTP.");

      // Extract the Relay server data from the Join Allocation response.
      var relayServerData = new RelayServerData(_playerAllocation, "udp");

      // Create NetworkSettings using the Relay server data.
      var settings = new NetworkSettings();
      settings.WithRelayParameters(ref relayServerData);
      _networkDriver = NetworkDriver.Create(settings);

      // Bind to the Relay server.
      if (_networkDriver.Bind(NetworkEndPoint.AnyIpv4) != 0)
      {
        Debug.LogError("Player client failed to bind");
      }
      else
      {
        Debug.Log("Player client bound to Relay server");
      }
    }
    
    public async Task SendJoinUntilAckReceived()
    {
      Task timeout = Task.Delay(1000);
      while (!timeout.IsCompleted)
      {
        SendSingleJoinMsg();
        await Task.Delay(200);

        if (_joinAcked)
        {
          break;
        }
      }
    }

    private void SendSingleJoinMsg()
    {
      _fbb.Clear();
      var content = Messages.JoinGameMsgContent.CreateJoinGameMsgContent(_fbb, _lobbyClientConnection.PlayerSlot);
      _fbb.Finish(content.Value);
      var msg = _fbb.DataBuffer.ToSizedArray();

      _fbb.Clear();
      var msgContentOffset = Messages.GameMessage.CreateContentVector(_fbb, msg);
      var finalMsg = ConnectionUtil.CreateGameMsg(_fbb, GameMessageType.JoinGame, msgContentOffset);
      _fbb.Finish(finalMsg.Value);

      SendMessageData(_fbb.DataBuffer.ToSizedArray(), _clientConnection);
    }

    public override int GetPlayerSlot()
    {
      return _lobbyClientConnection.PlayerSlot;
    }

    public override NetworkConnection GetPeerConnection()
    {
      return _clientConnection;
    }
  } 
}
