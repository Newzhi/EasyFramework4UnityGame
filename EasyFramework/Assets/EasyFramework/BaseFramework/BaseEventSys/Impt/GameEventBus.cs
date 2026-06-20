using System;
using System.Collections.Generic;
using UnityEngine;


namespace BaseFramework.BaseEventSys
{
    /// <summary>
    /// 轻量级全局事件总线。
    /// 适用于 UI、音频、任务、世界状态等低频/中频的模块间通知；不建议承载每帧海量数据流。
    /// </summary>
    public static class GameEventBus
    {
        private const int MinPublishBufferCapacity = 4;

        // 字典存储结构，List 便于避免重复绑定并保留订阅顺序。
        private static readonly Dictionary<Type, List<Delegate>> Handles = new Dictionary<Type, List<Delegate>>();

        // 线程静态发布缓冲：替代 handlers.ToArray()，避免每次 Publish 分配数组。
        // 按发布深度分配独立缓冲，支持事件回调中继续发布其它事件。
        [ThreadStatic]
        private static Delegate[][] _publishBuffers;

        [ThreadStatic]
        private static int _publishDepth;

        /// <summary>当前有订阅者的事件类型数量。</summary>
        public static int EventTypeCount => Handles.Count;

        /// <summary>
        /// 订阅指定事件类型。
        /// 同一个 handler 重复订阅只会保留一份。
        /// 推荐在 OnEnable/初始化阶段订阅，并在 OnDisable/销毁阶段退订。
        /// </summary>
        public static void RegisterEvent<T>(Action<T> handler) where T : IGameEvent
        {
            if (handler == null)
            {
                return;
            }

            Type type = typeof(T);
            if (!Handles.TryGetValue(type, out List<Delegate> handlers))
            {
                handlers = new List<Delegate>();
                Handles[type] = handlers;
            }

            if (!handlers.Contains(handler))
            {
                handlers.Add(handler);
            }
        }

        /// <summary>
        /// 退订指定事件类型。
        /// 通常应与 Subscribe 成对出现在生命周期函数中。
        /// </summary>
        public static void DeRegisterEvent<T>(Action<T> handler) where T : IGameEvent
        {
            if (handler == null)
            {
                return;
            }

            Type type = typeof(T);
            if (!Handles.TryGetValue(type, out List<Delegate> handlers))
            {
                return;
            }

            handlers.Remove(handler);
            if (handlers.Count == 0)
            {
                Handles.Remove(type);
            }
        }

        /// <summary>
        /// 发布事件。
        /// 发布开始时会创建一份 handler 快照；发布期间新增的订阅者不会收到本次事件，发布期间退订的订阅者如果已进入快照仍可能收到本次事件。
        /// 单个 handler 抛异常不会中断后续 handler。
        /// </summary>
        public static void SentEvent<T>(T eventArgs) where T : IGameEvent
        {
            Type type = typeof(T);
            if (!Handles.TryGetValue(type, out List<Delegate> handlers) || handlers.Count == 0)
            {
                return;
            }

            int count = handlers.Count;
            int depth = _publishDepth++;
            Delegate[] buffer = EnsurePublishBuffer(count, depth);

            for (int i = 0; i < count; i++)
            {
                buffer[i] = handlers[i];
            }

            try
            {
                for (int i = 0; i < count; i++)
                {
                    if (buffer[i] is Action<T> action)
                    {
                        InvokeHandlerSafely(action, eventArgs, type);
                    }
                }
            }
            finally
            {
                for (int i = 0; i < count; i++)
                {
                    buffer[i] = null;
                }
                _publishDepth--;
            }
        }

        /// <summary>清空所有事件订阅。通常在业务明确的生命周期边界手动调用。</summary>
        public static void ClearAll()
        {
            Handles.Clear();
        }

        /// <summary>清空指定事件类型的所有订阅。</summary>
        public static void ClearByType<T>() where T : IGameEvent
        {
            Handles.Remove(typeof(T));
        }

        /// <summary>获取指定事件类型当前订阅者数量，用于调试重复订阅或泄漏。</summary>
        public static int GetSubscriberCount<T>() where T : IGameEvent
        {
            return Handles.TryGetValue(typeof(T), out List<Delegate> handlers) ? handlers.Count : 0;
        }

        private static Delegate[] EnsurePublishBuffer(int requiredCount, int depth)
        {
            Delegate[][] buffers = _publishBuffers;
            if (buffers == null || buffers.Length <= depth)
            {
                int newLength = NextPowerOfTwo(Math.Max(MinPublishBufferCapacity, depth + 1));
                var expanded = new Delegate[newLength][];
                if (buffers != null)
                {
                    Array.Copy(buffers, expanded, buffers.Length);
                }
                _publishBuffers = expanded;
                buffers = expanded;
            }

            Delegate[] buffer = buffers[depth];
            if (buffer != null && buffer.Length >= requiredCount)
            {
                return buffer;
            }

            int capacity = NextPowerOfTwo(Math.Max(MinPublishBufferCapacity, requiredCount));
            buffer = new Delegate[capacity];
            buffers[depth] = buffer;
            return buffer;
        }

        private static int NextPowerOfTwo(int value)
        {
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1;
        }

        private static void InvokeHandlerSafely<T>(Action<T> action, T eventArgs, Type eventType) where T : IGameEvent
        {
            try
            {
                action.Invoke(eventArgs);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameEventBus] Handler exception. event={eventType.FullName}, handler={DescribeHandler(action)}, error={ex}");
            }
        }

        private static string DescribeHandler(Delegate handler)
        {
            if (handler == null)
            {
                return "<null>";
            }

            string typeName = handler.Method.DeclaringType != null ? handler.Method.DeclaringType.FullName : "<unknown type>";
            return $"{typeName}.{handler.Method.Name}";
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnEnterPlayMode]
        private static void ClearOnEnterPlayMode(UnityEditor.EnterPlayModeOptions options)
        {
            ClearAll();
        }
#endif
    }
}
