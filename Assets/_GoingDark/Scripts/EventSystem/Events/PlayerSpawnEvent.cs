using UnityEngine;
using UnityEngine.EventSystems;

public class PlayerSpawnEvent : BaseEventData
{
    public static readonly ExecuteEvents.EventFunction<IEventListener> Delegate = delegate (IEventListener handler, BaseEventData data)
    {
        var casted = ExecuteEvents.ValidateEventData<PlayerSpawnEvent>(data);
        handler.PlayerSpawnCallback(casted);
    };

    public ulong player;

    public PlayerSpawnEvent(EventSystem eventSystem, ulong player) : base(eventSystem)
    {
        this.player = player;
    }
}