using UnityEngine;
using UnityEngine.EventSystems;

public class InputReloadEvent : BaseEventData
{
    public static readonly ExecuteEvents.EventFunction<IEventListener> Delegate = delegate (IEventListener handler, BaseEventData data)
    {
        var casted = ExecuteEvents.ValidateEventData<InputReloadEvent>(data);
        handler.InputReloadCallback(casted);
    };

    public bool previous, current;

    public InputReloadEvent(EventSystem eventSystem, bool previous, bool current) : base(eventSystem)
    {
        this.previous = previous;
        this.current = current;
    }
}