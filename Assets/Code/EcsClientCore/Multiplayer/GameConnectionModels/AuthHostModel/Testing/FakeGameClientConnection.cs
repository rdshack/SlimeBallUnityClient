using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlatBuffers;
using Messages;

namespace Indigo.EcsClientCore
{
  public class FakeGameClientConnection : IGameClientConnection, IFakeUdpEndpoint
  {
    private FakeUdpConnection _fakeUdpConnection;

    private DateTime _lastTimeJoinMsgSent;

    private bool _joinAcked;

    private ILobbyClientConnection _lobbyClientConnection;
    private FlatBufferBuilder      _fbb = new FlatBufferBuilder(100);
    
    private List<int>               _tempFrameNumList   = new List<int>();
    private List<long>              _tempTimestampsList = new List<long>();
    private List<Offset<ByteArray>> _byteArrayOffets    = new List<Offset<ByteArray>>();
    
    private Queue<ArraySegment<byte>> _receivedMessages = new Queue<ArraySegment<byte>>();
    
    public event Action<AuthInputContent> OnAuthInputReceived;

    public FakeGameClientConnection(ILobbyClientConnection lobbyClientConnection, FakeRelay fakeRelay)
    {
      _lobbyClientConnection = lobbyClientConnection;
      fakeRelay.JoinGame(this);
      
      _lobbyClientConnection.OnJoinAcked += JoinAcked;
    }
    

    private void JoinAcked()
    {
      _joinAcked = true;
    }
    

    public async Task<bool> JoinGame(string joinCode)
    {
      while (!_joinAcked)
      {
        SendJoinMsg(); 
        await Task.Delay(200);
      }
      
      return true;
    }

    private void SendJoinMsg()
    {
      _fbb.Clear();
      var content = Messages.JoinGameMsgContent.CreateJoinGameMsgContent(_fbb, _lobbyClientConnection.PlayerSlot);
      _fbb.Finish(content.Value);
      var msg = _fbb.DataBuffer.ToSizedArray();

      _fbb.Clear();
      var msgContentOffset = Messages.GameMessage.CreateContentVector(_fbb, msg);
      var finalMsg =ConnectionUtil.CreateGameMsg(_fbb, GameMessageType.JoinGame, msgContentOffset);
      _fbb.Finish(finalMsg.Value);
      _fakeUdpConnection.Send(_fbb.DataBuffer.ToSizedArray());
    }

    public void ReceiveMessage(ArraySegment<byte> data, string connectionId)
    {
      _receivedMessages.Enqueue(data);
    }

    public void Update()
    {
      _fakeUdpConnection.Update();
      ReadReceived();
    }

    private void ReadReceived()
    {
      while (_receivedMessages.Count > 0)
      {
        var msgData = _receivedMessages.Dequeue();
        
        var msg = Messages.GameMessage.GetRootAsGameMessage(new ByteBuffer(msgData.Array, msgData.Offset));

        if (msg.Type == GameMessageType.AuthInput)
        {
          var contentTyped =
            Messages.AuthInputContent.GetRootAsAuthInputContent(new ByteBuffer(msg.GetContentArray()));

          OnAuthInputReceived?.Invoke(contentTyped);
        }

        //hostLatestMessageReceived = msg.ToString();
        break;
      }
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

      _fakeUdpConnection.Send(_fbb.DataBuffer.ToArraySegment(_fbb.DataBuffer.Position, _fbb.DataBuffer.Length - _fbb.DataBuffer.Position));
    }
    
    public bool AcceptConnection(IFakeUdpEndpoint sourcePoint, string connectionId)
    {
      _fakeUdpConnection = new FakeUdpConnection(connectionId, sourcePoint, false);
      return true;
    }

    public int PlayerSlot
    {
      get { return _lobbyClientConnection.PlayerSlot; }
    }
  } 
}
