using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlatBuffers;
using Messages;
using Unity.Networking.Transport;
using UnityEngine;

namespace Indigo.EcsClientCore
{
  public class P2PFakeGameClientConnection : P2PFakeGameConnectionBase, IP2PGameConnection
  {
    private ILobbyClientConnection _lobbyClientConnection;
    private bool                   _joinAcked;
    
    public event Action<Messages.P2PPlayerSyncContent, string, long> OnPeerSyncReceived;

    public P2PFakeGameClientConnection(ILobbyClientConnection clientConnection, FakeRelay fakeRelay)
    {
      _lobbyClientConnection = clientConnection;
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
      var finalMsg = ConnectionUtil.CreateGameMsg(_fbb, GameMessageType.JoinGame, msgContentOffset);
      _fbb.Finish(finalMsg.Value);
      
      _fakeUdpConnection.Send(_fbb.DataBuffer.ToArraySegment(_fbb.DataBuffer.Position, _fbb.DataBuffer.Length - _fbb.DataBuffer.Position));
    }
    
    public void Update()
    {
      _fakeUdpConnection?.Update();
      
      while (_receivedMessages.Count > 0)
      {
        var receivedMsg = _receivedMessages.Dequeue();
        var msgData = receivedMsg.data;
        
        var msg = Messages.GameMessage.GetRootAsGameMessage(new ByteBuffer(msgData.Array, msgData.Offset));
        
        if (msg.Type == GameMessageType.P2PSyncContent)
        {
          var contentTyped =
            Messages.P2PPlayerSyncContent.GetRootAsP2PPlayerSyncContent(new ByteBuffer(msg.GetContentArray()));

          OnPeerSyncReceived?.Invoke(contentTyped, msg.MsgId, msg.MsgCreationTime);
        }
                        
        //hostLatestMessageReceived = msg.ToString();
        break;
      }
    }

    public override int GetPlayerSlot()
    {
      return _lobbyClientConnection.PlayerSlot;
    }
  } 
}
