using System;
using System.Collections.Generic;
using Nox.CCK.Mods;
using Nox.CCK.Utils;


namespace Nox.ModLoader.Cores.Events {

    public class EventAPI : CCK.Mods.Events.IEventAPI {
        private readonly ModLoader.Mods.Mod _mod;
        private readonly CCK.Mods.Events.EventEntryFlags _channel;
        /// <summary>
        /// Subscriptions, copy-on-write: every mutation replaces the array instead of editing it in
        /// place. A dispatch can therefore iterate it directly — no snapshot allocation, and no
        /// "collection was modified" error when a callback subscribes or unsubscribes mid-dispatch.
        /// </summary>
        private EventSubscription[] _subscriptions = Array.Empty<EventSubscription>();

        internal EventAPI(ModLoader.Mods.Mod mod, CCK.Mods.Events.EventEntryFlags channel) {
            _mod = mod;
            _channel = channel;
        }

        // ReSharper disable Unity.PerformanceAnalysis
        private void Receive(EventContext context) {
            // Copy-on-write: the field is never edited in place, so reading it into a local gives the
            // dispatch a stable view of the subscriptions for its whole duration.
            var subscriptions = _subscriptions;

            // The payload is built on the first interested subscriber only. An Emit is broadcast to
            // every mod, so building it up-front allocated one EventData per mod per event even when
            // nobody was listening for that event name (measured: ~40k allocations over ten frames).
            EventData data = null;

            foreach (var sub in subscriptions)
                if (sub.EventName == null || sub.EventName == context.EventName) {
                    data ??= new EventData {
                            EventName        = context.EventName,
                            Data             = context.Data,
                            InternalSource   = context.Source,
                            SourceChannel    = context.Channel,
                            CallbackFunction = context.Callback ?? (_ => _mod.CoreAPI.LoggerAPI.LogDebug($"Event {context.EventName} had no callback function set."))
                        };

                    try {
                        sub.Callback(data);
                    } catch (Exception e) {
                        Logger.LogError($"Error while invoking event callback: {e} of event {context.EventName}");
                        Logger.LogError(e);
                    }
                }
        }

        private void Emit(EventContext context) {
            var ctx = new EventContext(context) { CurrentChannel = _channel, Source = _mod };
            var mod = context.Destination != null ? ModManager.GetMod(context.Destination) : null;
            if (mod != null) mod.CoreAPI.EventAPI.Receive(ctx);
            else
                foreach (var imod in ModManager.GetMods())
                    if (context.Channel.HasFlag(CCK.Mods.Events.EventEntryFlags.Main))
                        imod.CoreAPI?.EventAPI.Receive(ctx);
        }

        public void Emit(CCK.Mods.Events.EventContext context)
            => Emit(new EventContext(context));

        public void Emit(Dictionary<string, object> context)
            => Emit(EventContext.From(context));


        public void Emit(string eventName)
            => Emit(eventName, Array.Empty<object>());

        public void Emit(string eventName, params object[] data) {
            data ??= Array.Empty<object>();
            if (data.Length > 0 && data[^1] is Action<object[]>)
                Emit(
                    new EventContext {
                        Data = data.Length > 1 ? data[..^1] : Array.Empty<object>(), Destination = null, EventName = eventName, Source = _mod,
                        CurrentChannel = _channel, Channel = _channel, Callback = data[^1] as Action<object[]>
                    }
                );
            else
                Emit(
                    new EventContext {
                        Data = data, Destination = null, EventName = eventName, Source = _mod,
                        CurrentChannel = _channel, Channel = _channel, Callback = _ => { }
                    }
                );
        }

        public CCK.Mods.Events.EventSubscription Subscribe(string eventName, CCK.Mods.Events.EventCallback callback)
            => Subscribe(new EventSubscription { EventName = eventName, Callback = callback });

        public CCK.Mods.Events.EventSubscription Subscribe(CCK.Mods.Events.EventSubscription eventSub)
            => Subscribe(new EventSubscription(eventSub));

        private CCK.Mods.Events.EventSubscription Subscribe(EventSubscription eventSub) {
            if (HasUid(eventSub.UID)) {
                eventSub.UID = 0;
                while (HasUid(eventSub.UID) || eventSub.UID == uint.MaxValue) eventSub.UID++;
                if (eventSub.UID == uint.MaxValue) return null;
            }

            var subscriptions = new EventSubscription[_subscriptions.Length + 1];
            Array.Copy(_subscriptions, subscriptions, _subscriptions.Length);
            subscriptions[_subscriptions.Length] = eventSub;
            Array.Sort(subscriptions, (a, b) => a.Weight.CompareTo(b.Weight));
            _subscriptions = subscriptions;
            return eventSub;
        }

        private bool HasUid(uint uid) {
            foreach (var sub in _subscriptions)
                if (sub != null && sub.UID == uid)
                    return true;

            return false;
        }

        public CCK.Mods.Events.EventSubscription Subscribe(Dictionary<string, object> eventSub)
            => Subscribe(EventSubscription.From(eventSub));

        public void Unsubscribe(CCK.Mods.Events.EventSubscription eventSub)
            => Unsubscribe(eventSub.UID);

        internal void Unsubscribe(EventSubscription eventSub) {
            var index = Array.IndexOf(_subscriptions, eventSub);
            if (index < 0)
                return;

            var subscriptions = new EventSubscription[_subscriptions.Length - 1];
            Array.Copy(_subscriptions, 0, subscriptions, 0, index);
            Array.Copy(_subscriptions, index + 1, subscriptions, index, _subscriptions.Length - index - 1);
            _subscriptions = subscriptions;
        }

        public void Unsubscribe(uint uid)
            => RemoveAll(sub => sub.UID == uid);

        public void UnsubscribeAll()
            => _subscriptions = Array.Empty<EventSubscription>();

        public void UnsubscribeAll(string eventName)
            => RemoveAll(sub => sub.EventName == eventName);

        /// <summary>
        /// Drops every subscription matching <paramref name="predicate"/>, copy-on-write: the array
        /// currently being iterated by an in-flight dispatch is left untouched.
        /// </summary>
        private void RemoveAll(Predicate<EventSubscription> predicate) {
            var remaining = 0;
            foreach (var sub in _subscriptions)
                if (!predicate(sub))
                    remaining++;

            if (remaining == _subscriptions.Length)
                return;

            var subscriptions = new EventSubscription[remaining];
            var index         = 0;
            foreach (var sub in _subscriptions)
                if (!predicate(sub))
                    subscriptions[index++] = sub;

            _subscriptions = subscriptions;
        }
    }

    public class EventSubscription : CCK.Mods.Events.EventSubscription {
        internal EventSubscription() {
        }

        internal EventSubscription(CCK.Mods.Events.EventSubscription subscription) {
            UID = subscription.UID;
            EventName = subscription.EventName;
            Weight = subscription.Weight;
            Callback = subscription.Callback;
        }

        public uint UID { get; internal set; }
        public string EventName { get; internal set; }
        public uint Weight { get; internal set; }
        public CCK.Mods.Events.EventCallback Callback { get; internal set; }

        public static EventSubscription From(Dictionary<string, object> data) {
            if (data == null) return null;
            return new EventSubscription { UID = data.TryGetValue("uid", out var uid) && uid is uint u ? u : 0, EventName = data.TryGetValue("event_name", out var name) && name is string n ? n : null, Weight = data.TryGetValue("weight", out var weight) && weight is uint w ? w : 0, Callback = data.TryGetValue("callback", out var callback) && callback is CCK.Mods.Events.EventCallback cb ? cb : null };
        }
    }

    public class EventContext : CCK.Mods.Events.EventContext {
        internal EventContext() {
        }

        internal EventContext(CCK.Mods.Events.EventContext context) {
            Data = context.Data;
            Destination = context.Destination;
            EventName = context.EventName;
            Channel = context.Channel;
            Callback = context.Callback;
        }

        public object[] Data { get; internal set; }
        public string Destination { get; internal set; }
        public string EventName { get; internal set; }
        public CCK.Mods.Events.EventEntryFlags Channel { get; internal set; }
        public Action<object[]> Callback { get; internal set; }
        public ModLoader.Mods.Mod Source { get; internal set; }

        internal CCK.Mods.Events.EventEntryFlags CurrentChannel;

        public static EventContext From(Dictionary<string, object> data) {
            if (data == null) return null;
            return new EventContext {
                Data = data.TryGetValue("data", out var d) && d is object[] arr ? arr : Array.Empty<object>(), Destination = data.TryGetValue("destination", out var dest) && dest is string s ? s : null, EventName = data.TryGetValue("event_name", out var name) && name is string n ? n : null, Channel = data.TryGetValue("channel", out var channel) && channel is CCK.Mods.Events.EventEntryFlags c ? c : CCK.Mods.Events.EventEntryFlags.Main,
                Source = data.TryGetValue("source", out var source) && source is ModLoader.Mods.Mod mod ? mod : null, Callback = data.TryGetValue("callback", out var callback) && callback is Action<object[]> cb ? cb : null
            };
        }
    }

    public class EventData : CCK.Mods.Events.EventData {
        public string EventName { get; internal set; }
        public object[] Data { get; internal set; }
        public CCK.Mods.Events.EventEntryFlags SourceChannel { get; internal set; }
        public Action<object[]> CallbackFunction { get; internal set; }

        public bool TryGet<T>(int index, out T value) {
            if (Data == null) {
                value = default;
                return false;
            }


            if (Data.Length > index && Data[index] is T val) {
                value = val;
                return true;
            }

            value = default;
            return false;
        }

        public ModLoader.Mods.Mod InternalSource { get; internal set; }

        public IMod Source
            => InternalSource;

        public void Callback(params object[] args)
            => CallbackFunction(args);
    }

}