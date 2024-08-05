using System;
using System.Collections;
using System.Collections.Generic;
using ecs;
using Indigo.Slimeball;
using Indigo.SlimeBallClient;
using Indigo.EcsClientCore;
using UnityEngine;
using Logger = Indigo.EcsClientCore.Logger;

public class ClientLogger
{
    [Flags]
    public enum ClientLogFlags
    {
        None                 = 0,
        Sync = 1
    }
    
    private string         _prefix;
    private ClientLogFlags _flags;

    public ClientLogger(string prefix, ClientLogFlags flags)
    {
        _prefix = prefix;
        _flags = flags;
    }

    public ClientLogFlags GetLogFlags()
    {
        return _flags;
    }

    public bool HasFlag(ClientLogFlags flag)
    {
        return (_flags & flag) == flag;
    }

    public void Log(ClientLogFlags categories, string s)
    {
        if ((_flags & categories) != ClientLogFlags.None)
        {
            Log(s);
        }
    }

    public void Log(string s)
    {
        //Debug.Log($"{Time.time}: {_prefix}: {s}");
    }
        
    public void LogError(string s)
    {
        //Debug.LogError($"{_prefix}: {s}");
    }

    public void LogWarning(string s)
    {
        //Debug.LogWarning($"{_prefix}: {s}");
    }
}

public class EntryPoint : MonoBehaviour
{
    public bool                      InitAsSinglePlayer;
    public LocalWorldManager         Host;
    public ViewProcessor             HostViewProcessor;
    public LocalWorldManager         Client;
    public ViewProcessor             ClientViewProcessor;
    public TempPlayerViewBindingData ViewBindingData;

    private List<int>                _players = new List<int>();
    private P2PPlayerNetworkAdapter  _networkSyncAdapter;

    private AuthWorldManager _authWorld;
    
    void Start()
    {
        Debug.developerConsoleVisible = true;
        
        _players.Add(0);
        _players.Add(1);

        if (InitAsSinglePlayer)
        {
            InitHostLocalOnly();
        }
    }
    
    private void InitHostLocalOnly()
    {
        ClientLogger clientLogger = new ClientLogger("Host", ClientLogger.ClientLogFlags.Sync);
        Logger logger = new Indigo.EcsClientCore.Logger("Host", LogFlags.None);
        IGame game = new SlimeBallGame();
        ClientViewProcessor.Init(logger, new Vector2(0, 0), 0, game.GetSettings().GetMsPerFrame());

        ClientSyncer syncer = new ClientSyncer(2, clientLogger);
        Host.Init(game, ClientViewProcessor, logger, _players, 0, true, syncer, true);
    }

    public void InitHost(IP2PGameHostConnection gameHostConnection)
    {
        ClientLogger clientLogger = new ClientLogger("Host", ClientLogger.ClientLogFlags.Sync);
        Logger logger = new Indigo.EcsClientCore.Logger("Host", LogFlags.None);
        IGame game = new SlimeBallGame();
        HostViewProcessor.Init(logger, new Vector2(0, 0), 0, game.GetSettings().GetMsPerFrame());

        ClientSyncer syncer = new ClientSyncer(2, clientLogger);
        _networkSyncAdapter =
            new P2PPlayerNetworkAdapter(gameHostConnection, syncer, new FlatBufferFrameSerializer(), 0);
        
        //auth world needs its own game instance
        _authWorld = new AuthWorldManager(new SlimeBallGame(), syncer, 2);
        
        Host.Init(game, HostViewProcessor, logger, _players, 0, true, syncer);
    }

    public void InitClient(IP2PGameConnection gameClientConnection)
    {
        ClientLogger clientLogger = new ClientLogger("Client", ClientLogger.ClientLogFlags.Sync);
        Logger logger = new Indigo.EcsClientCore.Logger("Client", LogFlags.None);
        IGame game = new SlimeBallGame();
        ClientViewProcessor.Init(logger, new Vector2(0, 0), 1, game.GetSettings().GetMsPerFrame());

        ClientSyncer syncer = new ClientSyncer(2, clientLogger);
        _networkSyncAdapter =
            new P2PPlayerNetworkAdapter(gameClientConnection, syncer, new FlatBufferFrameSerializer(), 1);
        
        //auth world needs its own game instance
        _authWorld = new AuthWorldManager(new SlimeBallGame(), syncer, 2);
        
        Client.Init(game, ClientViewProcessor, logger, _players, 1, false, syncer);
    }

    private void Update()
    {
        _networkSyncAdapter?.Update();
    }
}
