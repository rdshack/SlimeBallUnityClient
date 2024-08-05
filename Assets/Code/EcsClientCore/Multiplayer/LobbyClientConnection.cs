using System;
using System.Threading.Tasks;
using FlatBuffers;
using Messages;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Networking.Transport.Utilities;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace Indigo.EcsClientCore
{
    public interface ILobbyClientConnection
    {
        Task<bool>                                JoinGame(string joinCode);
        void                                      Update();
        void                                      Dispose();
        int                                       PlayerSlot { get; }
        
        event Action<JoinLobbyResponseMsgContent> OnHostJoinResponse;
        event Action<PlayerJoinedLobbyMsgContent> OnUpdatePlayersMessage;
        event Action<LoadGameMsgContent>          OnLoadGameMsgContent;
        event Action                              OnJoinAcked;
        event Action                              OnStartGame;
    }

    public class LobbyClientConnection : RelayConnectionBase, ILobbyClientConnection
    {
        public event Action<JoinLobbyResponseMsgContent> OnHostJoinResponse;
        public event Action<PlayerJoinedLobbyMsgContent> OnUpdatePlayersMessage;
        public event Action<LoadGameMsgContent>          OnLoadGameMsgContent;
        public event Action                              OnJoinAcked;
        public event Action                              OnStartGame;

        public string JoinCode;
        public string _playerScreenName = "";

        private JoinAllocation playerAllocation;
        NetworkConnection      _clientConnection;
        
        public int PlayerSlot { get; private set; }

        public LobbyClientConnection(string playerScreenName, ClientLogger logger) : base(logger)
        {
            _playerScreenName = playerScreenName;
        }

        public async Task<bool> JoinGame(string joinCode)
        {
            // Input join code in the respective input field first.
            if (string.IsNullOrEmpty(joinCode))
            {
                Debug.LogError("Please input a join code.");
                return false;
            }

            Debug.Log("Player - Joining host allocation using join code. Upon success, I have 10 seconds to BIND to the Relay server that I've allocated.");

            try
            {
                playerAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
                Debug.Log("Player Allocation ID: " + playerAllocation.AllocationId);
            }
            catch (RelayServiceException ex)
            {
                Debug.LogError(ex.Message + "\n" + ex.StackTrace);
            }

            BindPlayer();
            ConnectPlayer();

            Task timeout = Task.Delay(2000);
            while (!timeout.IsCompleted)
            {
                await Task.Delay(100);

                if (_networkDriver.GetConnectionState(_clientConnection) == NetworkConnection.State.Connected)
                {
                    break;
                }
            }

            bool connected = _networkDriver.GetConnectionState(_clientConnection) == NetworkConnection.State.Connected;
            if (connected)
            {
                SendJoinMessage();
            }

            return connected;
        }

        public void LeaveLobby()
        {
            // This sends a disconnect event to the Host client,
            // letting them know they are disconnecting.
            _networkDriver.Disconnect(_clientConnection);

            // We remove the reference to the current connection by overriding it.
            _clientConnection = default(NetworkConnection);
        }

        private void BindPlayer()
        {
            Debug.Log("Player - Binding to the Relay server using UTP.");

            // Extract the Relay server data from the Join Allocation response.
            var relayServerData = new RelayServerData(playerAllocation, "udp");

            // Create NetworkSettings using the Relay server data.
            var settings = new NetworkSettings();
            settings.WithRelayParameters(ref relayServerData);
            settings.WithReliableStageParameters();

            // Create the Player's NetworkDriver from the NetworkSettings object.
            _networkDriver = NetworkDriver.Create(settings);
            _networkDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage));

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

        private void ConnectPlayer()
        {
            Debug.Log("Player - Connecting to Host's client.");

            // Sends a connection request to the Host Player.
            _clientConnection = _networkDriver.Connect();
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
            SendMessageData(_fbb.DataBuffer.ToSizedArray(), _clientConnection);
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
            ProcessAllReceivedMessageData(_clientConnection, MessageHandler);
        }

        private void MessageHandler(NetworkConnection connection, NetworkEvent.Type eventType, byte[] contentBuffer,
                                    int               contentLength)
        {
            switch (eventType)
            {
                // Handle Relay events.
                case NetworkEvent.Type.Data:

                    int offset = contentBuffer.Length - contentLength;
                    var lobbyMessage = Messages.LobbyMessage.GetRootAsLobbyMessage(new ByteBuffer(contentBuffer, offset));
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

                    //hostLatestMessageReceived = msg.ToString();
                    break;

                // Handle Disconnect events.
                case NetworkEvent.Type.Disconnect:
                    Debug.Log("Disconnected lobby connection");
                    _clientConnection = default(NetworkConnection);
                    break;
            }
        }

        public void Disconnect()
        {
            _networkDriver.Disconnect(_clientConnection);
        }
    }   
}