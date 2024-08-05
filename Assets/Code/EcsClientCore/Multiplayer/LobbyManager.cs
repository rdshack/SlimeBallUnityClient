using System;
using System.Threading.Tasks;
using Messages;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Indigo.EcsClientCore
{
  public interface ILobbyManager
  {
    Task                         HostGame();
    Task<ILobbyHostConnection>   InitHostConnection(string   hostScreenName);
    Task<ILobbyClientConnection> InitClientConnection(string playerScreenName);
  }

  public class LobbyManager : MonoBehaviour, ILobbyManager
  {
    public ConnectionUi Ui;
    
    private ILobbyHostConnection   _lobbyHostConnection;
    private LobbyClientConnection _clientLobbyConnection;
    private P2PGameHostConnection    _gameHostConnection;
    private P2PGamePlayerConnection   _gameClientConnection;

    private void Awake()
    {
      DontDestroyOnLoad(this);
      Ui.AssignLobbyManager(this);
      
      Screen.SetResolution(1280, 720, false);
    }

    public async Task<ILobbyHostConnection> InitHostConnection(string hostScreenName)
    {
      await UnityServices.InitializeAsync();
      await SignIn();

      var logger = new ClientLogger("Client", ClientLogger.ClientLogFlags.Sync);
      _lobbyHostConnection = new LobbyHostConnection(hostScreenName, logger);
      _gameHostConnection =  new P2PGameHostConnection(_lobbyHostConnection, logger); 
      
      _gameHostConnection.OnGameStartSent += HandleGameStartSent;

      await _lobbyHostConnection.Init();
      return _lobbyHostConnection;
    }
    
    private void HandleGameStartSent()
    {
      FindObjectOfType<EntryPoint>().InitHost(_gameHostConnection);
    }
    
    public async Task<ILobbyClientConnection> InitClientConnection(string playerScreenName)
    {
      await UnityServices.InitializeAsync();
      await SignIn();

      var logger = new ClientLogger("Client", ClientLogger.ClientLogFlags.Sync);
      _clientLobbyConnection = new LobbyClientConnection(playerScreenName, logger);
      _gameClientConnection = new P2PGamePlayerConnection(_clientLobbyConnection, logger); 

      _clientLobbyConnection.OnLoadGameMsgContent += HandleLoadMsg;
      _clientLobbyConnection.OnStartGame += HandleClientStartGame;
      
      return _clientLobbyConnection;
    }
    
    private void HandleClientStartGame()
    {
      FindObjectOfType<EntryPoint>().InitClient(_gameClientConnection);
    }

    private void HandleLoadMsg(LoadGameMsgContent data)
    {
      //1. Establish new non-reliable-non-ordered udp connection to game host
      //2. Load level scene
      //3. Send Join Message over new connection
      //4. Await Join Message Ack, resend Join Message on timer until received.
      //5. Await StartGame Message (sent over lobby connection).
      //6. Spawn in player locally and begin simulation!

      _ = BeginLoadProcess(data.JoinCode);
    }

    private async Task BeginLoadProcess(string joinCode)
    {
      Debug.Log($"trying to join game as client with code {joinCode}");
      bool success = await _gameClientConnection.JoinGame(joinCode);
      Debug.Log($"joined game connection as client result: {success}");
      SceneManager.LoadScene(2);
      await _gameClientConnection.SendJoinUntilAckReceived();
    }

    private async Task SignIn()
    {
      await AuthenticationService.Instance.SignInAnonymouslyAsync();
      Debug.Log($"Signed in. Player ID: {AuthenticationService.Instance.PlayerId}");
    }
    
    public async Task HostGame()
    {
      string code = await _gameHostConnection.HostRelayServer(_lobbyHostConnection.RegionId);
      //send to lobby players
      _lobbyHostConnection.SendLoadGameMessage(code);

      SceneManager.LoadScene(2);
      _gameHostConnection.SetHostReady();
    }

    private void Update()
    {
      _lobbyHostConnection?.Update();
      _clientLobbyConnection?.Update();
      _gameHostConnection?.Update();
      _gameClientConnection?.Update();
    }

    private void OnApplicationQuit()
    {
      _lobbyHostConnection?.Dispose();
      _clientLobbyConnection?.Dispose();
      _gameHostConnection?.Dispose();
      _gameClientConnection?.Dispose();
    }
  } 
}
