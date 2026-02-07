using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NetworkManagerUI : MonoBehaviour
{
    [SerializeField]
    private Button serverButton, hostButton, clientButton, relayButton;
    [SerializeField]
    private TextMeshProUGUI stateText;

    private void Awake()
    {
        // Networking disabled: hide controls
        if (serverButton) serverButton.gameObject.SetActive(false);
        if (hostButton) hostButton.gameObject.SetActive(false);
        if (clientButton) clientButton.gameObject.SetActive(false);
    }

    private void Start()
    {
        // no lobby/relay in single-player
    }

    private void UpdateState(string newState)
    {
        stateText.text = newState;
    }

    private void MatchFound()
    {
        if (relayButton) relayButton.gameObject.SetActive(false);
    }
}
