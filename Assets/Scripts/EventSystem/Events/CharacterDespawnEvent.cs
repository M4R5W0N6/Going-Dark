using UnityEngine;
using UnityEngine.EventSystems;

public class CharacterDespawnEvent : BaseEventData
{
    public static readonly ExecuteEvents.EventFunction<IEventListener> Delegate = delegate (IEventListener handler, BaseEventData data)
    {
        var casted = ExecuteEvents.ValidateEventData<CharacterDespawnEvent>(data);
        handler.CharacterDespawnCallback(casted);
    };

    public ulong player, enemy;

    public CharacterDespawnEvent(EventSystem eventSystem, ulong player, ulong enemy) : base(eventSystem)
    {
        this.player = player;
        this.enemy = enemy;
    }
}