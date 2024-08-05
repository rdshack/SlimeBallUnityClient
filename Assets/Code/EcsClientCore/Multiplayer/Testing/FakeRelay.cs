using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Indigo.EcsClientCore
{
  public class FakeRelay : MonoBehaviour
  {
    private IFakeUdpEndpoint       _lobbyHost;
    private List<IFakeUdpEndpoint> _lobbyClients = new List<IFakeUdpEndpoint>();

    private IFakeUdpEndpoint       _gameHost;
    private List<IFakeUdpEndpoint> _gameClients = new List<IFakeUdpEndpoint>();
  
    public void HostLobby(IFakeUdpEndpoint host)
    {
      _lobbyHost = host;
    }

    public bool JoinLobby(IFakeUdpEndpoint client)
    {
      string connectionId = Guid.NewGuid().ToString();
      _lobbyClients.Add(client);
    
      if (_lobbyHost.AcceptConnection(client, connectionId))
      {
        return client.AcceptConnection(_lobbyHost, connectionId);
      }

      return false;
    }
  
    public void HostGame(IFakeUdpEndpoint host)
    {
      _gameHost = host;
    }

    public bool JoinGame(IFakeUdpEndpoint client)
    {
      string connectionId = Guid.NewGuid().ToString();
      _gameClients.Add(client);

      if (_gameHost.AcceptConnection(client, connectionId))
      {
        return client.AcceptConnection(_gameHost, connectionId);
      }

      return false;
    }
  } 
}
