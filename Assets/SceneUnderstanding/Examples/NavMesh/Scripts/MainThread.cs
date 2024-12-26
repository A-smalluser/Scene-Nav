using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.MixedReality.SceneUnderstanding.Samples.Unity
{
    public class MainThread : MonoBehaviour
    {
        public static MainThread Instance { get; private set; }
        private readonly Queue<Action> executionQueue = new Queue<Action>();
        private readonly object queueLock = new object();

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        void Update()
        {
            lock (queueLock)
            {
                while (executionQueue.Count > 0)
                {
                    try
                    {
                        executionQueue.Dequeue().Invoke();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"执行队列中的操作时发生错误: {e}");
                    }
                }
            }
        }

        public void Enqueue(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            lock (queueLock)
            {
                executionQueue.Enqueue(action);
            }
        }

        public Task EnqueueAsync(Action action)
        {
            var tcs = new TaskCompletionSource<bool>();

            if (action == null)
            {
                tcs.SetException(new ArgumentNullException(nameof(action)));
                return tcs.Task;
            }

            Enqueue(() =>
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            });

            return tcs.Task;
        }

        public async Task<T> EnqueueAsync<T>(Func<T> function)
        {
            if (function == null)
            {
                throw new ArgumentNullException(nameof(function));
            }

            var tcs = new TaskCompletionSource<T>();

            Enqueue(() =>
            {
                try
                {
                    var result = function();
                    tcs.SetResult(result);
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            });

            return await tcs.Task;
        }
    }
}