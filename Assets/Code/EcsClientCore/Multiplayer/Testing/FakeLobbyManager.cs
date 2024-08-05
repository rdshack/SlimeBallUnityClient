using System.Threading.Tasks;
using Messages;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Indigo.EcsClientCore
{
  public class FakeLobbyManager : MonoBehaviour, ILobbyManager
  {
    public FakeRelay        FakeRelay;
    public ConnectionUi     Ui;
    public FakeLobbyManager ClientLobbyManager;

    private FakeLobbyHostConnection _lobbyHostConnection;
    private P2PFakeGameHostConnection  _gameHostConnection;

    private FakeLobbyClientConnection _lobbyClientConnection;
    private P2PFakeGameClientConnection  _gameClientConnection;

    private void Awake()
    {
      DontDestroyOnLoad(this);
      Ui.AssignLobbyManager(this);
    }

    public async Task<ILobbyHostConnection> InitHostConnection(string hostScreenName)
    {
      _lobbyHostConnection = new FakeLobbyHostConnection(hostScreenName, FakeRelay);
      _gameHostConnection =  new P2PFakeGameHostConnection(_lobbyHostConnection, FakeRelay);

      _gameHostConnection.OnGameStartSent += HandleGameStartSent;

      await _lobbyHostConnection.Init();
      return _lobbyHostConnection;
    }

    public async Task<ILobbyClientConnection> InitClientConnection(string playerScreenName)
    {
      _lobbyClientConnection = new FakeLobbyClientConnection(playerScreenName, FakeRelay);
      _gameClientConnection = new P2PFakeGameClientConnection(_lobbyClientConnection, FakeRelay);

      _lobbyClientConnection.OnLoadGameMsgContent += HandleLoadMsg;
      _lobbyClientConnection.OnStartGame += HandleClientStartGame;
      
      return _lobbyClientConnection;
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
      //we don't load for fake setup, since it's all 1 machine, and host is already loading scene
    }
    
    public async Task HostGame()
    {
      string code = await _gameHostConnection.HostRelayServer(_lobbyHostConnection.RegionId);

      _lobbyHostConnection.SendLoadGameMessage(code);

      SceneManager.LoadScene(1);
      _gameHostConnection.SetHostReady();
      
      //fake client saying it's ready
      await ClientLobbyManager.SendClientReady();
    }
    
    private void HandleGameStartSent()
    {
      FindObjectOfType<EntryPoint>().InitHost(_gameHostConnection);
    }

    private async Task SendClientReady()
    {
      await _lobbyClientConnection.JoinGame("");
      await _gameClientConnection.JoinGame("");
    }

    private void Update()
    {
      _lobbyHostConnection?.Update();
      _lobbyClientConnection?.Update();
      _gameClientConnection?.Update();
      _gameHostConnection?.Update();
    }
  } 
}
