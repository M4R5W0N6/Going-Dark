using UnityEngine;
using UnityEngine.EventSystems;

public class CharacterSpawnEvent : BaseEventData
{
    public static readonly ExecuteEvents.EventFunction<IEventListener> Delegate = delegate (IEventListener handler, BaseEventData data)
    {
        var casted = ExecuteEvents.ValidateEventData<CharacterSpawnEvent>(data);
        handler.CharacterSpawnCallback(casted);
    };

    public ulong player;

    public CharacterSpawnEvent(EventSystem eventSystem, ulong player) : base(eventSystem)
    {
        this.player = player;
    }
}