using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlatBuffers;
using Messages;

namespace Indigo.EcsClientCore
{
    public class FakeLobbyClientConnection : ILobbyClientConnection, IFakeUdpEndpoint
    {
        public event Action<JoinLobbyResponseMsgContent> OnHostJoinResponse;
        public event Action<PlayerJoinedLobbyMsgContent> OnUpdatePlayersMessage;
        public event Action<LoadGameMsgContent>          OnLoadGameMsgContent;
        public event Action                              OnJoinAcked;
        public event Action                              OnStartGame;
        
        private FlatBufferBuilder _fbb = new FlatBufferBuilder(100);
        private FakeUdpConnection _fakeUdpConnection;
        
        private Queue<ArraySegment<byte>> _receivedMessages = new Queue<ArraySegment<byte>>();

        public string JoinCode;
        public string _playerScreenName = "";
        

        public int  PlayerSlot { get; private set; }

        public FakeLobbyClientConnection(string playerScreenName, FakeRelay fakeRelay)
        {
            _playerScreenName = playerScreenName;
            fakeRelay.JoinLobby(this);
        }

        public async Task<bool> JoinGame(string joinCode)
        {
            await Task.Delay(10);
            SendJoinMessage();
            return true;
        }
        
        public void Dispose()
        {

        }
        
        public void ReceiveMessage(ArraySegment<byte> data, string connectionId)
        {
            _receivedMessages.Enqueue(data);
        }
        
        private void ReadReceived()
        {
            while (_receivedMessages.Count > 0)
            {
                var contentBuffer = _receivedMessages.Dequeue();
                
                var lobbyMessage = Messages.LobbyMessage.GetRootAsLobbyMessage(new ByteBuffer(contentBuffer.Array, contentBuffer.Offset));

                if (lobbyMessage.Type == LobbyMessageType.JoinLobbyResponse)
                {
                    var contentTyped =
                        Messages.JoinLobbyResponseMsgContent.GetRootAsJoinLobbyResponseMsgContent(new ByteBuffer(lobbyMessage.GetContentArray()));

                    PlayerSlot = contentTyped.YourPlayerSlot;
                    OnHostJoinResponse?.Invoke(contentTyped);
                }
                else if (lobbyMessage.Type == LobbyMessageType.PlayerJoinedLobby)
                {
                    var contentTyped =
                        Messages.PlayerJoinedLobbyMsgContent.GetRootAsPlayerJoinedLobbyMsgContent(new ByteBuffer(lobbyMessage.GetContentArray()));
                    
                    OnUpdatePlayersMessage?.Invoke(contentTyped);
                }
                else if (lobbyMessage.Type == LobbyMessageType.LoadGame)
                {
                    var contentTyped =
                        Messages.LoadGameMsgContent.GetRootAsLoadGameMsgContent(new ByteBuffer(lobbyMessage.GetContentArray()));

                    OnLoadGameMsgContent?.Invoke(contentTyped);
                }
                else if (lobbyMessage.Type == LobbyMessageType.JoinGameAck)
                {
                    OnJoinAcked?.Invoke();
                }
                else if (lobbyMessage.Type == LobbyMessageType.StartGame)
                {
                    OnStartGame?.Invoke();
                }

                break;
            }
        }

        private void SendJoinMessage()
        {
            _fbb.Clear();
            var content = Messages.JoinLobbyMsgContent.CreateJoinLobbyMsgContent(_fbb, _fbb.CreateString(_playerScreenName));
            _fbb.Finish(content.Value);
            var msg = _fbb.DataBuffer.ToSizedArray();

            _fbb.Clear();
            var msgContentOffset = Messages.LobbyMessage.CreateContentVector(_fbb, msg);
            var finalMsg = Messages.LobbyMessage.CreateLobbyMessage(_fbb, LobbyMessageType.JoinLobby, msgContentOffset);
            _fbb.Finish(finalMsg.Value);
            _fakeUdpConnection.Send(_fbb.DataBuffer.ToArraySegment(_fbb.DataBuffer.Position, _fbb.DataBuffer.Length - _fbb.DataBuffer.Position));
        }

        public void Update()
        {
            ReadReceived();
            _fakeUdpConnection?.Update();
        }

        public bool AcceptConnection(IFakeUdpEndpoint sourcePoint, string connectionId)
        {
            _fakeUdpConnection = new FakeUdpConnection(connectionId, sourcePoint, true);
            return true;
        }
    }   
}