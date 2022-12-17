using UnityEngine;
using UnityEngine.EventSystems;

public class CharacterIsReloadingEvent : BaseEventData
{
    public static readonly ExecuteEvents.EventFunction<IEventListener> Delegate = delegate (IEventListener handler, BaseEventData data)
    {
        var casted = ExecuteEvents.ValidateEventData<CharacterIsReloadingEvent>(data);
        handler.CharacterIsReloadingCallback(casted);
    };

    public bool previous, current;

    public CharacterIsReloadingEvent(EventSystem eventSystem, bool previous, bool current) : base(eventSystem)
    {
        this.previous = previous;
        this.current = current;
    }
}