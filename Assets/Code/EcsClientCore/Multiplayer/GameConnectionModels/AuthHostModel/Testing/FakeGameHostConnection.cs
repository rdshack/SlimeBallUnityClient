using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlatBuffers;
using Messages;

namespace Indigo.EcsClientCore
{
  public class FakeGameHostConnection : IGameHostConnection, IFakeUdpEndpoint
  {
    private ILobbyHostConnection    _lobbyHostConnection;

    private FlatBufferBuilder  _fbb              = new FlatBufferBuilder(100);
    private Queue<ReceivedMsg> _receivedMessages = new Queue<ReceivedMsg>();
    
    private List<FakeUdpConnection>               _fakeUdpConnections = new List<FakeUdpConnection>();
    private Dictionary<string, FakeUdpConnection> _udpConnectionById  = new Dictionary<string, FakeUdpConnection>();
    private Dictionary<string, int>               _udpToPlayerSlot    = new Dictionary<string, int>();
    private Dictionary<int, string>               _playerSlotToUdp    = new Dictionary<int, string>();
    
    private bool                                  _sentGameStartEvent;
    
    private List<Offset<ByteArray>> _byteArrayOffets  = new List<Offset<ByteArray>>();
    
    private List<int>               _readyPlayerSlots = new List<int>();
    
    public event Action<string>                      OnClientJoined;
    public event Action                              OnGameStartSent;
    public event Action<Messages.PlayerInputContent> OnPlayerInputReceived;

    public FakeGameHostConnection(ILobbyHostConnection lobbyHostConnection, FakeRelay fakeRelay)
    {
      _lobbyHostConnection = lobbyHostConnection;
      fakeRelay.HostGame(this);
    }

    public void SetHostReady()
    {
      _readyPlayerSlots.Add(0);
    }


    public async Task<string> HostRelayServer(string regionId)
    {
      await Task.Delay(100);
      return "";
    }

    public bool AcceptConnection(IFakeUdpEndpoint sourcePoint, string connectionId)
    {
      if(_udpConnectionById.ContainsKey(connectionId))
      {
        return false;
      }

      var udpConnection = new FakeUdpConnection(connectionId, sourcePoint, false);
      _fakeUdpConnections.Add(udpConnection);
      _udpConnectionById.Add(connectionId, udpConnection);
      
      return true;
    }

    public void ReceiveMessage(ArraySegment<byte> data, string connectionId)
    {
      _receivedMessages.Enqueue(new ReceivedMsg(){ data = data, connectionId = connectionId});
    }
    
    public void Update()
    {
      foreach (var fakeUdp in _fakeUdpConnections)
      {
        fakeUdp.Update(); 
      }
      
      while (_receivedMessages.Count > 0)
      {
        var receivedMsg = _receivedMessages.Dequeue();
        var msgData = receivedMsg.data;
        
        var msg = Messages.GameMessage.GetRootAsGameMessage(new ByteBuffer(msgData.Array, msgData.Offset));

        if (msg.Type == GameMessageType.JoinGame)
        {
          var contentTyped =
            Messages.JoinGameMsgContent.GetRootAsJoinGameMsgContent(new ByteBuffer(msg.GetContentArray()));
          
          _readyPlayerSlots.Add(contentTyped.Slot);
          _udpToPlayerSlot[receivedMsg.connectionId] = contentTyped.Slot;
          _playerSlotToUdp[contentTyped.Slot] = receivedMsg.connectionId;

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

          OnPlayerInputReceived?.Invoke(contentTyped);
        }
                        
        //hostLatestMessageReceived = msg.ToString();
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

      var curConnection = _playerSlotToUdp[playerId];
      _udpConnectionById[curConnection].Send(_fbb.DataBuffer.ToArraySegment(_fbb.DataBuffer.Position, _fbb.DataBuffer.Length - _fbb.DataBuffer.Position));
    }
  } 
}
