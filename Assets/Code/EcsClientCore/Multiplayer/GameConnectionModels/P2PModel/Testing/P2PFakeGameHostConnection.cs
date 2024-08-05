using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlatBuffers;
using Messages;
using Unity.Networking.Transport;
using UnityEngine;

namespace Indigo.EcsClientCore
{
  public class P2PFakeGameHostConnection : P2PFakeGameConnectionBase, IP2PGameHostConnection
  {
    private const int MAX_CONNECTIONS = 1;
    
    private ILobbyHostConnection _lobbyHostConnection;
    
    public string JoinCode;
    
    private int _connectionId;
    private NetworkConnection _hostConnection;

    private List<int> _readyPlayerSlots = new List<int>();
    
    private bool _sentGameStartEvent;
    
    public event Action                                OnGameStartSent;
    public event Action<string>                        OnClientJoined;
    public event Action<Messages.P2PPlayerSyncContent, string, long> OnPeerSyncReceived;

    public P2PFakeGameHostConnection(ILobbyHostConnection lobbyHostConnection, FakeRelay fakeRelay)
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
    
    public void Update()
    {
      _fakeUdpConnection?.Update();
      
      while (_receivedMessages.Count > 0)
      {
        var receivedMsg = _receivedMessages.Dequeue();
        var msgData = receivedMsg.data;
        
        var msg = Messages.GameMessage.GetRootAsGameMessage(new ByteBuffer(msgData.Array, msgData.Offset));

        if (msg.Type == GameMessageType.JoinGame)
        {
          var contentArray = msg.GetContentArray();
          var contentTyped =
            Messages.JoinGameMsgContent.GetRootAsJoinGameMsgContent(new ByteBuffer(contentArray));
          
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

          OnPeerSyncReceived?.Invoke(contentTyped, msg.MsgId, msg.MsgCreationTime);
        }
                        
        //hostLatestMessageReceived = msg.ToString();
        break;
      }
    }

    public override int GetPlayerSlot()
    {
      return 0;
    }
  } 
}
