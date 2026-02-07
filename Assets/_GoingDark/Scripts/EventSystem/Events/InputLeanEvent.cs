using UnityEngine;
using UnityEngine.EventSystems;

public class InputLeanEvent : BaseEventData
{
    public static readonly ExecuteEvents.EventFunction<IEventListener> Delegate = delegate (IEventListener handler, BaseEventData data)
    {
        var casted = ExecuteEvents.ValidateEventData<InputLeanEvent>(data);
        handler.InputLeanCallback(casted);
    };

    public float previous, current;

    public InputLeanEvent(EventSystem eventSystem, float previous, float current) : base(eventSystem)
    {
        this.previous = previous;
        this.current = current;
    }
}