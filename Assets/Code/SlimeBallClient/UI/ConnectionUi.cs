using System;
using System.Collections.Generic;
using System.Linq;
using Messages;
using TMPro;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.UI;

namespace Indigo.EcsClientCore
{
    public class ConnectionUi : MonoBehaviour
    {
        public InputField NameInput;
        public Button     ProceedToHostMenuButton;
        public Button     ProceedToJoinMenuButton;
        
        public Dropdown   RegionsDropdown;
        public Button     HostButton;
        public Text       JoinCodeText;
        
        public InputField JoinCodeInput;
        public Button     JoinButton;
        
        public TextMeshProUGUI[] PlayerNameText;

        public Button          StartGameButton;
        public TextMeshProUGUI CodeText;

        public GameObject MainMenuPanel;
        public GameObject HostPanel;
        public GameObject PlayerPanel;
        public GameObject LobbyPanel;

        private ILobbyManager _lobbyManager;

        private ILobbyHostConnection   _hostConnection;
        private ILobbyClientConnection _clientConnection;
        private string                _regionId;

        // GUI vars
        int          regionAutoSelectIndex = 0;
        List<string> regionOptions         = new List<string>(); 
        List<Region> regions               = new List<Region>();

        // Control vars
        bool        isHost;
        bool        isPlayer;

        private void Start()
        {
            // Set GUI to Main Menu
            MainMenuPanel.SetActive(true);
            HostPanel.SetActive(false);
            PlayerPanel.SetActive(false);
            LobbyPanel.SetActive(false);
            StartGameButton.gameObject.SetActive(false);
            
            ProceedToHostMenuButton.onClick.AddListener(OnStartClientAsHost);
            ProceedToJoinMenuButton.onClick.AddListener(OnStartClientAsPlayer);
            
            HostButton.onClick.AddListener(OnHostGameButton);
            JoinButton.onClick.AddListener(OnJoinGameButton);
            StartGameButton.onClick.AddListener(OnStartGameButton);
        }

        public void AssignLobbyManager(ILobbyManager lobbyManager)
        {
            _lobbyManager = lobbyManager;
        }

        private async void OnStartGameButton()
        {
            await _lobbyManager.HostGame();
        }

        private async void OnHostGameButton()
        {
            _regionId = GetSelectedRegionId();
            await _hostConnection.HostLobby(GetSelectedRegionId());
            LobbyPanel.SetActive(true);
            PlayerNameText[0].text = NameInput.text;
            CodeText.text = _hostConnection.JoinCode;
            JoinCodeText.text = _hostConnection.JoinCode;
        }

        public async void OnStartClientAsHost()
        {
            _hostConnection = await _lobbyManager.InitHostConnection(NameInput.text);
            PopulateRegions(_hostConnection.Regions);

            _hostConnection.OnClientJoinedLobby += HandleClientJoinedLobby;
            
            MainMenuPanel.SetActive(false);
            HostPanel.SetActive(true);
            isHost = true;
        }

        private void HandleClientJoinedLobby(PlayerLobbyInfo player)
        {
            PlayerNameText[player.slot].text = player.name;

            if (isHost)
            {
                StartGameButton.gameObject.SetActive(true);   
            }
        }

        /// <summary>
        /// Event handler for when the Start game as Player client button is clicked.
        /// </summary>
        public async void OnStartClientAsPlayer()
        {
            _clientConnection = await _lobbyManager.InitClientConnection(NameInput.text);
            MainMenuPanel.SetActive(false);
            PlayerPanel.SetActive(true);
            isPlayer = true;

            _clientConnection.OnUpdatePlayersMessage += HandleUpdatePlayers;
        }

        private void HandleUpdatePlayers(PlayerJoinedLobbyMsgContent playerJoinedMsgContent)
        {
            for (int i = 0; i < playerJoinedMsgContent.PlayersLength; i++)
            {
                var playerInfo = playerJoinedMsgContent.Players(i).Value;
                PlayerNameText[playerInfo.Slot].text = playerInfo.ScreenName;
            }
        }

        private void PopulateRegions(List<Region> allRegions)
        {
            regions.Clear();
            regionOptions.Clear();
            foreach (var region in allRegions)
            {
                regionOptions.Add(region.Id);
                regions.Add(region);
            }
            
            RegionsDropdown.AddOptions(regionOptions);
            RegionsDropdown.interactable = true;
        }

        private string GetSelectedRegionId()
        {
            // Return null (indicating to auto-select the region/QoS) if regions list is empty OR auto-select/QoS is chosen
            if (!regions.Any() || RegionsDropdown.value == regionAutoSelectIndex)
            {
                return null;
            }
            // else use chosen region (offset -1 in dropdown due to first option being auto-select/QoS)
            return regions[RegionsDropdown.value - 1].Id;
        }


        private async void OnJoinGameButton()
        {
            bool success = await _clientConnection.JoinGame(JoinCodeInput.text);

            Debug.Log($"Join Lobby result success?: {success}");
            if (success)
            {
                LobbyPanel.SetActive(true);
            }
        }
    }   
}
