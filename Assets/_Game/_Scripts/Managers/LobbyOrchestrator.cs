using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

#pragma warning disable CS4014

/// <summary>
///     Lobby orchestrator. I put as much UI logic within the three sub screens,
///     but the transport and RPC logic remains here. It's possible we could pull
/// </summary>
public class LobbyOrchestrator : NetworkBehaviour {
    [SerializeField] private MainMatchScreen _mainMatchScreen; // Add Main Match Screen
    [SerializeField] private MainLobbyScreen _mainLobbyScreen;
    [SerializeField] private CreateLobbyScreen _createScreen;
    [SerializeField] private RoomScreen _roomScreen;

    private void Start() {
        _mainLobbyScreen.gameObject.SetActive(false);
        _createScreen.gameObject.SetActive(false);
        _roomScreen.gameObject.SetActive(false);
        _mainMatchScreen.gameObject.SetActive(true); // Add Main Match Screen

        CreateLobbyScreen.LobbyCreated += CreateLobby;
        LobbyRoomPanel.LobbySelected += OnLobbySelected;
        RoomScreen.LobbyLeft += OnLobbyLeft;
        RoomScreen.StartPressed += OnGameStart;
        MainMatchScreen.NormalSelected += OnNormalSelected; // Add Main Match Screen Actions
        MainMatchScreen.FriendSelected += OnFriendSelected; // Add Main Match Screen Actions
        MainMatchScreen.RankSelected += OnRankSelected; // Add Main Match Screen Actions

        
        NetworkObject.DestroyWithScene = true;
    }

    #region Main Match

    private void OnNormalSelected()
    {
        using (new Load("Joining Normal Game...")) {
            try {
                _mainMatchScreen.gameObject.SetActive(false);
                _mainLobbyScreen.gameObject.SetActive(true);
            }
            catch (Exception e) {
                Debug.LogError(e);
                CanvasUtilities.Instance.ShowError("Failed joining lobby");
            }
        }
    }

    private void OnRankSelected()
    {
        using (new Load("Joining Rank Game...")) {
            try {
                _mainMatchScreen.gameObject.SetActive(false);
                _mainLobbyScreen.gameObject.SetActive(true);
            }
            catch (Exception e) {
                Debug.LogError(e);
                CanvasUtilities.Instance.ShowError("Failed joining lobby");
            }
        }
    }

    private void OnFriendSelected()
    {
        using (new Load("Joining Friend Game...")) {
            try {
                _mainMatchScreen.gameObject.SetActive(false);
                _createScreen.gameObject.SetActive(true);
            }
            catch (Exception e) {
                Debug.LogError(e);
                CanvasUtilities.Instance.ShowError("Failed joining lobby");
            }
        }
    }

    #endregion

    #region Main Lobby

    private async void OnLobbySelected(Lobby lobby) {
        using (new Load("Joining Lobby...")) {
            try {
                await MatchmakingService.JoinLobbyWithAllocation(lobby.Id);

                _mainLobbyScreen.gameObject.SetActive(false);
                _mainLobbyScreen.gameObject.SetActive(true);

                NetworkManager.Singleton.StartClient();
            }
            catch (Exception e) {
                Debug.LogError(e);
                CanvasUtilities.Instance.ShowError("Failed joining lobby");
            }
        }
    }

    #endregion

    #region Create

    private async void CreateLobby(LobbyData data) {
        using (new Load("Creating Lobby...")) {
            try {
                await MatchmakingService.CreateLobbyWithAllocation(data);

                _createScreen.gameObject.SetActive(false);
                _roomScreen.gameObject.SetActive(true);

                // Starting the host immediately will keep the relay server alive
                NetworkManager.Singleton.StartHost();
            }
            catch (Exception e) {
                Debug.LogError(e);
                CanvasUtilities.Instance.ShowError("Failed creating lobby");
            }
        }
    }

    #endregion

    #region Room

    private readonly Dictionary<ulong, bool> _playersInLobby = new();
    public static event Action<Dictionary<ulong, bool>> LobbyPlayersUpdated;
    private float _nextLobbyUpdate;

    public override void OnNetworkSpawn() {
        if (IsServer) {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnectedCallback;
            _playersInLobby.Add(NetworkManager.Singleton.LocalClientId, false);
            UpdateInterface();
        }

        // Client uses this in case host destroys the lobby
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnectCallback;

 
    }

    private void OnClientConnectedCallback(ulong playerId) {
        if (!IsServer) return;

        // Add locally
        if (!_playersInLobby.ContainsKey(playerId)) _playersInLobby.Add(playerId, false);

        PropagateToClients();

        UpdateInterface();
    }

    private void PropagateToClients() {
        foreach (var player in _playersInLobby) UpdatePlayerClientRpc(player.Key, player.Value);
    }

    [ClientRpc]
    private void UpdatePlayerClientRpc(ulong clientId, bool isReady) {
        if (IsServer) return;

        if (!_playersInLobby.ContainsKey(clientId)) _playersInLobby.Add(clientId, isReady);
        else _playersInLobby[clientId] = isReady;
        UpdateInterface();
    }

    private void OnClientDisconnectCallback(ulong playerId) {
        if (IsServer) {
            // Handle locally
            if (_playersInLobby.ContainsKey(playerId)) _playersInLobby.Remove(playerId);

            // Propagate all clients
            RemovePlayerClientRpc(playerId);

            UpdateInterface();
        }
        else {
            // This happens when the host disconnects the lobby
            _roomScreen.gameObject.SetActive(false);
            _mainLobbyScreen.gameObject.SetActive(true);
            OnLobbyLeft();
        }
    }

    [ClientRpc]
    private void RemovePlayerClientRpc(ulong clientId) {
        if (IsServer) return;

        if (_playersInLobby.ContainsKey(clientId)) _playersInLobby.Remove(clientId);
        UpdateInterface();
    }

    public void OnReadyClicked() {
        SetReadyServerRpc(NetworkManager.Singleton.LocalClientId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetReadyServerRpc(ulong playerId) {
        _playersInLobby[playerId] = true;
        PropagateToClients();
        UpdateInterface();
    }

    private void UpdateInterface() {
        LobbyPlayersUpdated?.Invoke(_playersInLobby);
    }

    private async void OnLobbyLeft() {
        using (new Load("Leaving Lobby...")) {
            _playersInLobby.Clear();
            NetworkManager.Singleton.Shutdown();
            await MatchmakingService.LeaveLobby();
        }
    }
    
    public override void OnDestroy() {
     
        base.OnDestroy();
        CreateLobbyScreen.LobbyCreated -= CreateLobby;
        LobbyRoomPanel.LobbySelected -= OnLobbySelected;
        RoomScreen.LobbyLeft -= OnLobbyLeft;
        RoomScreen.StartPressed -= OnGameStart;
        
        // We only care about this during lobby
        if (NetworkManager.Singleton != null) {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnectCallback;
        }
      
    }
    
    private async void OnGameStart() {
        using (new Load("Starting the game...")) {
            await MatchmakingService.LockLobby();
            NetworkManager.Singleton.SceneManager.LoadScene("Game", LoadSceneMode.Single);
        }
    }

    #endregion
}