using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

[DefaultExecutionOrder(-500)]
public class FusionInputProvider : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField]
    private InputActionAsset actionsAsset;

    [SerializeField]
    private string moveActionName = "Move";

    [SerializeField]
    private string lookActionName = "Look";

    [SerializeField]
    private string fireActionName = "Fire";

    [SerializeField]
    private string reloadActionName = "Reload";

    [SerializeField]
    private string aimActionName = "Aim";

	[SerializeField]
	private string leanActionName = "CompositeLean";

    [SerializeField]
    private string sprintActionName = "Sprint";

    private readonly HashSet<NetworkRunner> registeredRunners = new HashSet<NetworkRunner>();
    private readonly List<NetworkRunner> activeRunners = new List<NetworkRunner>();
    private readonly List<NetworkRunner> staleRunners = new List<NetworkRunner>();

    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction fireAction;
    private InputAction reloadAction;
    private InputAction aimAction;
    private InputAction leanAction;
    private InputAction sprintAction;

    private void OnEnable()
    {
        ResolveActions();
        RefreshRunnerRegistrations();
    }

    private void OnDisable()
    {
        foreach (NetworkRunner runner in registeredRunners)
        {
            if (runner != null)
                runner.RemoveCallbacks(this);
        }

        registeredRunners.Clear();
        activeRunners.Clear();
        staleRunners.Clear();
    }

    private void Update()
    {
        ResolveActions();
        RefreshRunnerRegistrations();
    }

    private void RefreshRunnerRegistrations()
    {
        activeRunners.Clear();

        foreach (NetworkRunner runner in NetworkRunner.Instances)
        {
            if (runner == null)
                continue;

            activeRunners.Add(runner);

            if (registeredRunners.Add(runner))
                runner.AddCallbacks(this);
        }

        staleRunners.Clear();
        foreach (NetworkRunner runner in registeredRunners)
        {
            if (runner == null || !activeRunners.Contains(runner))
                staleRunners.Add(runner);
        }

        for (int i = 0; i < staleRunners.Count; i++)
        {
            NetworkRunner stale = staleRunners[i];
            if (stale != null)
                stale.RemoveCallbacks(this);
            registeredRunners.Remove(stale);
        }
    }

    private void ResolveActions()
    {
        if (actionsAsset == null)
        {
            PlayerInput playerInput = FindFirstObjectByType<PlayerInput>(FindObjectsInactive.Include);
            if (playerInput != null)
                actionsAsset = playerInput.actions;
        }

        if (actionsAsset == null)
        {
            InputSystemUIInputModule inputModule = FindFirstObjectByType<InputSystemUIInputModule>(FindObjectsInactive.Include);
            if (inputModule != null)
                actionsAsset = inputModule.actionsAsset;
        }

        if (actionsAsset == null)
            return;

        moveAction ??= FindAndEnableAction(moveActionName);
        lookAction ??= FindAndEnableAction(lookActionName);
        fireAction ??= FindAndEnableAction(fireActionName);
        reloadAction ??= FindAndEnableAction(reloadActionName);
        aimAction ??= FindAndEnableAction(aimActionName);
        leanAction ??= FindAndEnableAction(leanActionName);
        sprintAction ??= FindAndEnableAction(sprintActionName);
    }

    private InputAction FindAndEnableAction(string actionName)
    {
        InputAction action = actionsAsset.FindAction(actionName, false);
        if (action != null && !action.enabled)
            action.Enable();
        return action;
    }

    private bool TryBuildInput(out FusionNetworkInput result)
    {
        result = default;

        if (moveAction != null)
            result.Move = moveAction.ReadValue<Vector2>();

        if (lookAction != null)
            result.Look = lookAction.ReadValue<Vector2>();

        if (leanAction != null)
        {
            float rawLean = leanAction.ReadValue<float>();
            result.Lean = (rawLean * 0.5f) + 0.5f;
        }
        else
        {
            result.Lean = 0.5f;
        }

        bool hasAnyAction =
            moveAction != null ||
            lookAction != null ||
            fireAction != null ||
            reloadAction != null ||
            aimAction != null ||
            leanAction != null ||
            sprintAction != null;

        if (!hasAnyAction)
            return false;

        result.Buttons.Set(FusionNetworkInput.FireButton, fireAction != null && fireAction.IsPressed());
        result.Buttons.Set(FusionNetworkInput.ReloadButton, reloadAction != null && reloadAction.IsPressed());
        result.Buttons.Set(FusionNetworkInput.AimButton, aimAction != null && aimAction.IsPressed());
        result.Buttons.Set(FusionNetworkInput.SprintButton, sprintAction != null && sprintAction.IsPressed());

        return true;
    }

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        // In multi-peer, only the runner owning the currently presented scene should consume local device input.
        if (runner == null || !runner.ProvideInput || !IsPresentationRunner(runner))
        {
            input.Set(default(FusionNetworkInput));
            return;
        }

        if (!TryBuildInput(out FusionNetworkInput currentInput))
            currentInput = default;

        input.Set(currentInput);
    }

    private static bool IsPresentationRunner(NetworkRunner runner)
    {
        if (runner == null || !runner.IsRunning)
            return false;

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
            return runner.GetVisible();

        int cameraSceneHandle = mainCamera.gameObject.scene.handle;
        return runner.gameObject.scene.handle == cameraSceneHandle || runner.SimulationUnityScene.handle == cameraSceneHandle;
    }

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}
