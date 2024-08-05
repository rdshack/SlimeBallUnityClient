using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlatBuffers;
using Messages;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace Indigo.EcsClientCore
{
  public struct SerializedPlayerFrameInputData
  {
    public int    frameNum; 
    public long   locallyAppliedTimestamp;
    public byte[] serializedData;
    public int    serializedDataLength;
  }

  public interface IGameClientConnection
  {
    void Update();
    Task<bool>                     JoinGame(string                               joinCode);
    void                           SendNonAckedInputsToHost(List<SerializedPlayerFrameInputData> frameInputs, int lastAuthInputReceivedFromHost, int firstFrame);
    int                            PlayerSlot { get; }
    event Action<AuthInputContent> OnAuthInputReceived;
  }

  public enum GameClientConnectionState
  {
    PreJoin,
    SendingJoinUntilAck,
    Awaiting
  }

  public class GameClientConnection : RelayConnectionBase, IGameClientConnection
  {
    private ILobbyClientConnection _lobbyClientConnection;
    
    private JoinAllocation _playerAllocation;
    private NetworkConnection      _clientConnection;
    
    private List<int>               _tempFrameNumList   = new List<int>();
    private List<long>              _tempTimestampsList = new List<long>();
    private List<Offset<ByteArray>> _byteArrayOffets    = new List<Offset<ByteArray>>();
    
    private bool _joinAcked;

    public int PlayerSlot
    {
      get { return _lobbyClientConnection.PlayerSlot; }
    }

    public event Action<AuthInputContent> OnAuthInputReceived;

    public GameClientConnection(ILobbyClientConnection lobbyClientConnection, ClientLogger logger) : base(logger)
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
    
    
      private void MessageHandler(NetworkConnection connection, NetworkEvent.Type eventType, byte[] contentBuffer,
                                  int               contentLength)
      {
          switch (eventType)
          {
              // Handle Relay events.
              case NetworkEvent.Type.Data:

                  int offset = contentBuffer.Length - contentLength;
                  var gameMsg = Messages.GameMessage.GetRootAsGameMessage(new ByteBuffer(contentBuffer, offset));
                  if (gameMsg.Type == GameMessageType.AuthInput)
                  {
                    var contentTyped =
                      Messages.AuthInputContent.GetRootAsAuthInputContent(new ByteBuffer(gameMsg.GetContentArray()));

                    Debug.Log($"Received auth inputs");
                    OnAuthInputReceived?.Invoke(contentTyped);
                  }

                  //hostLatestMessageReceived = msg.ToString();
                  break;

              // Handle Disconnect events.
              case NetworkEvent.Type.Disconnect:
                  Debug.Log("Disconnected game connection");
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

    public void SendNonAckedInputsToHost(List<SerializedPlayerFrameInputData> frameInputs, int lastAuthInputReceivedFromHost, int firstFrame)
    {
      _fbb.Clear();
      _byteArrayOffets.Clear();
      _tempFrameNumList.Clear();
      _tempTimestampsList.Clear();
      
      foreach (var frameInput in frameInputs)
      {
        _tempFrameNumList.Add(frameInput.frameNum);
        _tempTimestampsList.Add(frameInput.locallyAppliedTimestamp);
        VectorOffset byteVectorOffset = FlatBufferUtil.AddVectorToBufferFromByteArray(_fbb, frameInput.serializedData, frameInput.serializedDataLength);
        _byteArrayOffets.Add(Messages.ByteArray.CreateByteArray(_fbb, byteVectorOffset));
      }
      
      VectorOffset framesOffset = FlatBufferUtil.AddVectorToBufferFromOffsetList(_fbb, Messages.PlayerInputContent.StartInputsVector, _byteArrayOffets);
      

      VectorOffset timestampsOffset = FlatBufferUtil.AddVectorToBufferFromLongList(_fbb, Messages.PlayerInputContent.StartFrameCreationTimestampsVector,
                                                                                   _tempTimestampsList);
      
      var content = Messages.PlayerInputContent.CreatePlayerInputContent(_fbb, 
                                                                         _lobbyClientConnection.PlayerSlot, 
                                                                         lastAuthInputReceivedFromHost,
                                                                         firstFrame, 
                                                                         framesOffset,
                                                                         timestampsOffset);
      _fbb.Finish(content.Value);
      var msg = _fbb.DataBuffer.ToSizedArray();
      
      _fbb.Clear();
      var msgContentOffset = Messages.GameMessage.CreateContentVector(_fbb, msg);
      var finalMsg = ConnectionUtil.CreateGameMsg(_fbb, GameMessageType.PlayerInput, msgContentOffset);
      _fbb.Finish(finalMsg.Value);

      SendMessageData(_fbb.DataBuffer.ToSizedArray(), _clientConnection);
    }

    private void ConnectPlayer()
    {
      // Sends a connection request to the Host Player.
      _clientConnection = _networkDriver.Connect();
      Debug.Log($"Player - Connecting to Host's client result: {_clientConnection!=default}");
    }
  }
 
}