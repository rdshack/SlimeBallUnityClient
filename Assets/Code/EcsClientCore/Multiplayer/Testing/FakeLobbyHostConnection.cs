using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlatBuffers;
using Messages;
using Unity.Services.Relay.Models;

namespace Indigo.EcsClientCore
{
  public class FakeLobbyHostConnection : ILobbyHostConnection, IFakeUdpEndpoint
  {
    public string RegionId { get; set; }
    public string JoinCode { get; set; }

    public string HostScreenName;

    private FlatBufferBuilder _fbb          = new FlatBufferBuilder(100); 
    private int               _nextOpenSlot = 1;

    private List<FakeUdpConnection>               _fakeUdpConnections       = new List<FakeUdpConnection>();
    private Dictionary<string, FakeUdpConnection> _udpConnectionById = new Dictionary<string, FakeUdpConnection>();
    private Dictionary<string, int>               _connectionIdToPlayerSlot = new Dictionary<string, int>();
    
    private Queue<ReceivedMsg> _receivedMessages = new Queue<ReceivedMsg>();

    public List<PlayerLobbyInfo> PlayerInfos { get; } = new List<PlayerLobbyInfo>();
    public List<Region>          Regions     { get; } = new List<Region>();

    public event Action<PlayerLobbyInfo> OnClientJoinedLobby;

    public FakeLobbyHostConnection(string hostScreenName, FakeRelay fakeRelay)
    {
      HostScreenName = hostScreenName;
      PlayerInfos.Add(new PlayerLobbyInfo(){ name = hostScreenName, slot = 0});
      fakeRelay.HostLobby(this);
    }

    public void Dispose()
    {

    }

    private async Task PopulateRegions()
    {
      Regions.Add(new Region("JimboWorld", "yeye"));
    }

    public async Task Init()
    {
      await PopulateRegions();
    }

    public void SendJoinGameAck(int contentTypedSlot)
    {
      _fbb.Clear();
      var finalMsg = Messages.LobbyMessage.CreateLobbyMessage(_fbb, LobbyMessageType.JoinGameAck);
      _fbb.Finish(finalMsg.Value);
      
      _fakeUdpConnections[0].Send(_fbb.DataBuffer.ToArraySegment(_fbb.DataBuffer.Position, _fbb.DataBuffer.Length - _fbb.DataBuffer.Position));
    }

    public Task HostLobby(string regionId)
    {
      RegionId = regionId;
      JoinCode = "PW";
      return Task.CompletedTask;
    }

    public bool AcceptConnection(IFakeUdpEndpoint sourcePoint, string connectionId)
    {
      if(_udpConnectionById.ContainsKey(connectionId))
      {
        return false;
      }

      var udpConnection = new FakeUdpConnection(connectionId, sourcePoint, true);
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
      UpdateHost();
    }

    private void UpdateHost()
    {
      ReadReceived();
      foreach (var fakeUdp in _fakeUdpConnections)
      {
        fakeUdp.Update();
      }
    }
    private void ReadReceived()
    {
      while (_receivedMessages.Count > 0)
      {
        var msg = _receivedMessages.Dequeue();
        var contentBuffer = msg.data;

        var lobbyMessage = Messages.LobbyMessage.GetRootAsLobbyMessage(new ByteBuffer(contentBuffer.Array, contentBuffer.Offset));
        if (lobbyMessage.Type == LobbyMessageType.JoinLobby)
        {
          var contentTyped =
            Messages.JoinLobbyMsgContent.GetRootAsJoinLobbyMsgContent(new ByteBuffer(lobbyMessage
                                                                                       .GetContentArray()));

          bool containsPlayerName = PlayerInfos.Exists(info => info.name == contentTyped.PlayerName);
          if (!containsPlayerName)
          {
            var newPlayer = new PlayerLobbyInfo() {name = contentTyped.PlayerName, slot = _nextOpenSlot++};
            _connectionIdToPlayerSlot[msg.connectionId] = newPlayer.slot;
            PlayerInfos.Add(newPlayer); 
            OnClientJoinedLobby?.Invoke(newPlayer);
          }

          bool success = !containsPlayerName;
          SendJoinResponse(success, _udpConnectionById[msg.connectionId]);
          SendPlayerJoinedMessageToAll();
        }

        break;
      }
    }

    private void SendJoinResponse(bool success, FakeUdpConnection udpConnection)
    {
      _fbb.Clear();
      
      var content = Messages.JoinLobbyResponseMsgContent.CreateJoinLobbyResponseMsgContent(_fbb, success, _connectionIdToPlayerSlot[udpConnection.ConnectionId]);
      _fbb.Finish(content.Value);
      var msg = _fbb.DataBuffer.ToSizedArray();

      _fbb.Clear();
      var msgContentOffset =  Messages.LobbyMessage.CreateContentVector(_fbb, msg);
      var finalMsg = Messages.LobbyMessage.CreateLobbyMessage(_fbb, LobbyMessageType.JoinLobbyResponse, msgContentOffset);
      _fbb.Finish(finalMsg.Value);

      udpConnection.Send(_fbb.DataBuffer.ToSizedArray());
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

      foreach (var curConnection in _fakeUdpConnections)
      {
        var dataToSend =
          _fbb.DataBuffer.ToArraySegment(_fbb.DataBuffer.Position, _fbb.DataBuffer.Length - _fbb.DataBuffer.Position);
        curConnection.Send(dataToSend); 
      }
    }
    
    public void SendGameStartToAll()
    {
      _fbb.Clear();
      var finalMsg = Messages.LobbyMessage.CreateLobbyMessage(_fbb, LobbyMessageType.StartGame);
      _fbb.Finish(finalMsg.Value);

      foreach (var curConnection in _fakeUdpConnections)
      {
        curConnection.Send(_fbb.DataBuffer.ToSizedArray()); 
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

      foreach (var curConnection in _fakeUdpConnections)
      {
        curConnection.Send(_fbb.DataBuffer.ToSizedArray()); 
      }
    }
  } 
}
