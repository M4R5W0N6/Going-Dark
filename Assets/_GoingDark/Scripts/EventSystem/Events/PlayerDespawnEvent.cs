using UnityEngine;
using UnityEngine.EventSystems;

public class PlayerDespawnEvent : BaseEventData
{
    public static readonly ExecuteEvents.EventFunction<IEventListener> Delegate = delegate (IEventListener handler, BaseEventData data)
    {
        var casted = ExecuteEvents.ValidateEventData<PlayerDespawnEvent>(data);
        handler.PlayerDespawnCallback(casted);
    };

    public ulong player;

    public PlayerDespawnEvent(EventSystem eventSystem, ulong player) : base(eventSystem)
    {
        this.player = player;
    }
}