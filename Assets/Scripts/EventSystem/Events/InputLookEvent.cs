using UnityEngine;
using UnityEngine.EventSystems;

public class InputLookEvent : BaseEventData
{
    public static readonly ExecuteEvents.EventFunction<IEventListener> Delegate = delegate (IEventListener handler, BaseEventData data)
    {
        var casted = ExecuteEvents.ValidateEventData<InputLookEvent>(data);
        handler.InputLookCallback(casted);
    };

    public Vector2 previous, current;

    public InputLookEvent(EventSystem eventSystem, Vector2 previous, Vector2 current) : base(eventSystem)
    {
        this.previous = previous;
        this.current = current;
    }
}