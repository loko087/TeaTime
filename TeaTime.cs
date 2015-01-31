﻿// TeaTime v0.5.4 alpha

// By Andrés Villalobos > andresalvivar@gmail.com > twitter.com/matnesis
// Special thanks to Antonio Zamora > twitter.com/tzamora
// Created 2014/12/26 12:21 am

// TeaTime is a fast & simple queue for timed callbacks, fashioned as a
// MonoBehaviour extension set, focused on solving common coroutines patterns in
// Unity games.

// Just put 'TeaTime.cs' somewhere in your project and call it inside any
// MonoBehaviour using 'this.tt'.


//    this.ttAdd("Queue name", 2, () =>
//    {
//        Debug.Log("2 seconds since start " + Time.time);
//    })
//    .ttLoop(3, delegate(ttHandler loop)
//    {       
//        // A loop will run frame by frame for all his duration. 
//        // loop.deltaTime holds a custom delta for interpolation.

//        Camera.main.backgroundColor 
//            = Color.Lerp(Camera.main.backgroundColor, Color.white, loop.t);
//    })
//    this.ttAdd("DOTween example", delegate(ttHandler t)
//    {
//        Sequence myTween = DOTween.Sequence();
//        myTween.Append(transform.DOMoveX(5, 2.5f));
//        myTween.Append(transform.DOMoveX(-5, 2.5f));

//        // WaitFor waits for a time or YieldInstruction after the current
//        // callback is done and before the next queued callback.
//        t.WaitFor(myTween.WaitForCompletion());
//    })
//    .ttAdd(() =>
//    {
//        Debug.Log("myTween end, +5 secs " + Time.time);
//    })
//    .ttNow(1, () =>
//    {
//        Debug.Log("ttNow is arbitrary and ignores the queue order " + Time.time);
//    })
//    .ttWaitForCompletion(); 
//    // Locks the current queue, ignoring new appends until all callbacks are done.


// Some important details:
// - Execution starts immediately
// - Queues are unique to his MonoBehaviour (this is an extension after all)
// - Naming your queue is recommended if you want to use more than one queue with safety
// - You can use a YieldInstruction instead of time (i.e. WaitForEndOfFrame)
// - ttWaitForCompletion ensures a complete and safe run during continuous calls
// - ttHandler adds special control features to your callbacks
// - You can create tween-like behaviours mixing loops, ttHandler.deltaTime and Lerp functions
// - ttHandler.waitFor applies only once and at the end of the current callback
// - ttNow will always ignore queues (it's inmune to ttWaitForCompletion)
// - Below the sugar, everything runs on coroutines!


// Copyright (c) 2014/12/26 andresalvivar@gmail.com

// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:

// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.

// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.


using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// Timed callback data.
/// </summary>
public class TeaTask
{
    public float time = 0f;
    public YieldInstruction yieldInstruction = null;
    public Action callback = null;
    public Action<ttHandler> callbackWithHandler = null;
    public bool isLoop = false;


    public TeaTask(float time, YieldInstruction yield, Action callback, Action<ttHandler> callbackWithHandler, bool isLoop)
    {
        this.time = time;
        this.yieldInstruction = yield;
        this.callback = callback;
        this.callbackWithHandler = callbackWithHandler;
        this.isLoop = isLoop;
    }
}


/// <summary>
/// TeaTime Handler.
/// </summary>
public class ttHandler
{
    public bool isActive = true;
    public float deltaTime = 0f;
    public float timeSinceStart = 0f;
    public YieldInstruction yieldToWait = null;


    /// <summary>
    /// Breaks the current loop.
    /// </summary>
    public void Break()
    {
        this.isActive = false;
    }


    /// <summary>
    /// Waits for a time interval after the current callback.
    /// </summary>
    public void WaitFor(float interval)
    {
        this.yieldToWait = new WaitForSeconds(interval);
    }


    /// <summary>
    /// Waits for a YieldInstruction after the current callback.
    /// </summary>
    public void WaitFor(YieldInstruction yieldToWait)
    {
        this.yieldToWait = yieldToWait;
    }
}


/// <summary>
/// TeaTime is a fast & simple queue for timed callbacks, fashioned as a
/// MonoBehaviour extension set, focused on solving common coroutines patterns in
/// Unity games.
/// </summary>
public static class TeaTime
{
    /// <summary>
    /// Main queue for all the timed callbacks.
    /// </summary>
    private static Dictionary<MonoBehaviour, Dictionary<string, List<TeaTask>>> queue;

    /// <summary>
    /// Holds the currently running queues.
    /// </summary>
    private static Dictionary<MonoBehaviour, List<string>> currentlyRunning;

    /// <summary>
    /// Holds the last queue name used.
    /// </summary>
    private static Dictionary<MonoBehaviour, string> lastQueueName;

    /// <summary>
    /// Holds the locked queues.
    /// </summary>
    private static Dictionary<MonoBehaviour, List<string>> lockedQueue;


    /// <summary>
    /// Prepares the main queue for the instance.
    /// </summary>
    private static void PrepareInstanceQueue(MonoBehaviour instance)
    {
        if (queue == null)
            queue = new Dictionary<MonoBehaviour, Dictionary<string, List<TeaTask>>>();

        if (queue.ContainsKey(instance) == false)
            queue.Add(instance, new Dictionary<string, List<TeaTask>>());
    }


    /// <summary>
    /// Prepares the last queue name for the instance.
    /// </summary>
    private static void PrepareInstanceLastQueueName(MonoBehaviour instance)
    {
        if (lastQueueName == null)
            lastQueueName = new Dictionary<MonoBehaviour, string>();

        // Default name
        if (lastQueueName.ContainsKey(instance) == false)
            lastQueueName[instance] = "TEATIME_DEFAULT_QUEUE_NAME";
    }


    /// <summary>
    /// Prepares the locked queue for the instance.
    /// </summary>
    private static void PrepareInstanceLockedQueue(MonoBehaviour instance)
    {
        if (lockedQueue == null)
            lockedQueue = new Dictionary<MonoBehaviour, List<string>>();

        if (lockedQueue.ContainsKey(instance) == false)
            lockedQueue.Add(instance, new List<string>());
    }


    /// <summary>
    /// Returns true if the queue is currently locked.
    /// </summary>
    private static bool IsLocked(MonoBehaviour instance, string queueName)
    {
        PrepareInstanceLockedQueue(instance);

        // It is?
        if (lockedQueue[instance].Contains(queueName))
            return true;

        return false;
    }


    /// <summary>
    ///// Appends a callback (timed or looped) into a queue.
    /// </summary>
    private static MonoBehaviour ttAdd(this MonoBehaviour instance, string queueName,
        float timeDelay, YieldInstruction yieldDelay,
        Action callback, Action<ttHandler> callbackWithHandler,
        bool isLoop)
    {
        // Ignore locked queues (but remember in his name)
        if (IsLocked(instance, queueName))
        {
            lastQueueName[instance] = queueName;
            return instance;
        }
        //else
        //{
        //    if (isLoop)
        //    {
        //        Debug.Log("Queue < ttAppendLoop " + queueName);
        //    }
        //    else
        //    {
        //        Debug.Log("Queue < ttAppend " + queueName);
        //    }
        //}

        PrepareInstanceQueue(instance);
        PrepareInstanceLastQueueName(instance);

        // Adds callback list & last queue name 
        lastQueueName[instance] = queueName;
        if (queue[instance].ContainsKey(queueName) == false)
            queue[instance].Add(queueName, new List<TeaTask>());

        // Appends a new task
        List<TeaTask> taskList = queue[instance][queueName];
        taskList.Add(new TeaTask(timeDelay, yieldDelay, callback, callbackWithHandler, isLoop));

        // Execute queue
        instance.StartCoroutine(ExecuteQueue(instance, queueName));

        return instance;
    }


    /// <summary>
    /// Appends a timed callback into a queue.
    /// </summary>
    public static MonoBehaviour ttAdd(this MonoBehaviour instance, string queueName, float timeDelay, Action callback)
    {
        return instance.ttAdd(queueName, timeDelay, null, callback, null, false);
    }


    /// <summary>
    /// Appends a timed callback into a queue.
    /// </summary>
    public static MonoBehaviour ttAdd(this MonoBehaviour instance, string queueName, float timeDelay, Action<ttHandler> callback)
    {
        return instance.ttAdd(queueName, timeDelay, null, null, callback, false);
    }


    /// <summary>
    /// Appends a timed callback into a queue.
    /// </summary>
    public static MonoBehaviour ttAdd(this MonoBehaviour instance, string queueName, YieldInstruction yieldToWait, Action callback)
    {
        return instance.ttAdd(queueName, 0, yieldToWait, callback, null, false);
    }


    /// <summary>
    /// Appends a timed callback into a queue.
    /// </summary>
    public static MonoBehaviour ttAdd(this MonoBehaviour instance, string queueName, YieldInstruction yieldToWait, Action<ttHandler> callback)
    {
        return instance.ttAdd(queueName, 0, yieldToWait, null, callback, false);
    }


    /// <summary>
    /// Appends a timed callback into the last used queue (or default).
    /// </summary>
    public static MonoBehaviour ttAdd(this MonoBehaviour instance, float timeDelay, Action callback)
    {
        PrepareInstanceLastQueueName(instance);

        return instance.ttAdd(lastQueueName[instance], timeDelay, null, callback, null, false);
    }


    /// <summary>
    /// Appends a timed callback into the last used queue (or default).
    /// </summary>
    public static MonoBehaviour ttAdd(this MonoBehaviour instance, float timeDelay, Action<ttHandler> callback)
    {
        PrepareInstanceLastQueueName(instance);

        return instance.ttAdd(lastQueueName[instance], timeDelay, null, null, callback, false);
    }


    /// <summary>
    /// Appends a timed callback into the last used queue (or default).
    /// </summary>
    public static MonoBehaviour ttAdd(this MonoBehaviour instance, YieldInstruction yieldToWait, Action callback)
    {
        PrepareInstanceLastQueueName(instance);

        return instance.ttAdd(lastQueueName[instance], 0, yieldToWait, callback, null, false);
    }


    /// <summary>
    /// Appends a timed callback into the last used queue (or default).
    /// </summary>
    public static MonoBehaviour ttAdd(this MonoBehaviour instance, YieldInstruction yieldToWait, Action<ttHandler> callback)
    {
        PrepareInstanceLastQueueName(instance);

        return instance.ttAdd(lastQueueName[instance], 0, yieldToWait, null, callback, false);
    }


    /// <summary>
    /// Appends a time interval into a queue.
    /// </summary>
    public static MonoBehaviour ttAdd(this MonoBehaviour instance, string queueName, float interval)
    {
        return instance.ttAdd(queueName, interval, null, null, null, false);
    }


    /// <summary>
    /// Appends a time interval into the last used queue (or default).
    /// </summary>
    public static MonoBehaviour ttAdd(this MonoBehaviour instance, float interval)
    {
        PrepareInstanceLastQueueName(instance);

        return instance.ttAdd(lastQueueName[instance], interval, null, null, null, false);
    }


    /// <summary>
    /// Appends a timed callback into a queue.
    /// </summary>
    public static MonoBehaviour ttAdd(this MonoBehaviour instance, string queueName, Action callback)
    {
        return instance.ttAdd(queueName, 0, null, callback, null, false);
    }


    /// <summary>
    /// Appends a timed callback into a queue.
    /// </summary>
    public static MonoBehaviour ttAdd(this MonoBehaviour instance, string queueName, Action<ttHandler> callback)
    {
        return instance.ttAdd(queueName, 0, null, null, callback, false);
    }


    /// <summary>
    /// Appends a timed callback into the last used queue (or default).
    /// </summary>
    public static MonoBehaviour ttAdd(this MonoBehaviour instance, Action callback)
    {
        PrepareInstanceLastQueueName(instance);

        return instance.ttAdd(lastQueueName[instance], 0, null, callback, null, false);
    }


    /// <summary>
    /// Appends a timed callback into the last used queue (or default).
    /// </summary>
    public static MonoBehaviour ttAdd(this MonoBehaviour instance, Action<ttHandler> callback)
    {
        PrepareInstanceLastQueueName(instance);

        return instance.ttAdd(lastQueueName[instance], 0, null, null, callback, false);
    }


    /// <summary>
    /// Appends a callback that runs frame by frame (until his exit is forced) into a queue.
    /// </summary>
    public static MonoBehaviour ttLoop(this MonoBehaviour instance, string queueName, float duration, Action<ttHandler> callback)
    {
        return instance.ttAdd(queueName, duration, null, null, callback, true);
    }


    /// <summary>
    /// Appends a callback that runs frame by frame (until his exit is forced) into a queue.
    /// </summary>
    public static MonoBehaviour ttLoop(this MonoBehaviour instance, string queueName, Action<ttHandler> callback)
    {
        return instance.ttAdd(queueName, 0, null, null, callback, true);
    }


    /// <summary>
    /// Appends a callback that runs frame by frame for his duration into the last used queue.
    /// </summary>
    public static MonoBehaviour ttLoop(this MonoBehaviour instance, float duration, Action<ttHandler> callback)
    {
        PrepareInstanceLastQueueName(instance);

        return instance.ttAdd(lastQueueName[instance], duration, null, null, callback, true);
    }


    /// <summary>
    /// Appends a callback that runs frame by frame (until his exit is forced) into the last used queue.
    /// </summary>
    public static MonoBehaviour ttLoop(this MonoBehaviour instance, Action<ttHandler> callback)
    {
        PrepareInstanceLastQueueName(instance);

        return instance.ttAdd(lastQueueName[instance], 0, null, null, callback, true);
    }


    /// <summary>
    /// Executes a timed callback ignoring queues.
    /// </summary>
    private static MonoBehaviour ttNow(this MonoBehaviour instance, float timeDelay, YieldInstruction yieldToWait, Action callback)
    {
        instance.StartCoroutine(ExecuteOnce(timeDelay, yieldToWait, callback, null));

        return instance;
    }


    /// <summary>
    /// Executes a timed callback ignoring queues.
    /// </summary>
    public static MonoBehaviour ttNow(this MonoBehaviour instance, float timeDelay, Action callback)
    {
        return instance.ttNow(timeDelay, null, callback);
    }


    /// <summary>
    /// Executes a timed callback ignoring queues.
    /// </summary>
    public static MonoBehaviour ttNow(this MonoBehaviour instance, YieldInstruction yieldToWait, Action callback)
    {
        return instance.ttNow(0, yieldToWait, callback);
    }


    /// <summary>
    /// Locks the current queue (no more appends) until all his callbacks are done.
    /// </summary>
    public static MonoBehaviour ttWaitForCompletion(this MonoBehaviour instance)
    {
        PrepareInstanceQueue(instance);
        PrepareInstanceLastQueueName(instance);

        // Ignore if the queue is empty
        if (queue[instance].ContainsKey(lastQueueName[instance]) == false ||
            queue[instance][lastQueueName[instance]].Count < 1)
            return instance;

        // Adds the lock
        if (IsLocked(instance, lastQueueName[instance]) == false)
            lockedQueue[instance].Add(lastQueueName[instance]);

        return instance;
    }


    /// <summary>
    /// Execute all timed callbacks and loops for the instance queue.
    /// </summary>
    private static IEnumerator ExecuteQueue(MonoBehaviour instance, string queueName)
    {
        // Ignore if empty
        if (queue.ContainsKey(instance) == false)
            yield break;

        if (queue[instance].ContainsKey(queueName) == false)
            yield break;

        // Create a runner list for the instance
        if (currentlyRunning == null)
            currentlyRunning = new Dictionary<MonoBehaviour, List<string>>();

        if (currentlyRunning.ContainsKey(instance) == false)
            currentlyRunning.Add(instance, new List<string>());

        // Ignore if already running
        if (currentlyRunning.ContainsKey(instance) && currentlyRunning[instance].Contains(queueName))
            yield break;

        // Locks the queue
        currentlyRunning[instance].Add(queueName);

        // Run until depleted (over a clone)
        List<TeaTask> batch = new List<TeaTask>();
        batch.AddRange(queue[instance][queueName]);

        foreach (TeaTask task in batch)
        {
            // Select, execute & remove tasks
            if (task.isLoop)
            {
                if (task.time > 0)
                {
                    yield return instance.StartCoroutine(ExecuteLoop(task.time, task.callbackWithHandler));
                }
                else
                {
                    yield return instance.StartCoroutine(ExecuteInfiniteLoop(task.callbackWithHandler));
                }
            }
            else
            {
                yield return instance.StartCoroutine(ExecuteOnce(task.time, task.yieldInstruction, task.callback, task.callbackWithHandler));
            }
            queue[instance][queueName].Remove(task);
        }

        // Unlocks the queue
        currentlyRunning[instance].Remove(queueName);

        // Try again is there are new items, else, remove the lock
        if (queue[instance][queueName].Count > 0)
        {
            instance.StartCoroutine(ExecuteQueue(instance, queueName));
        }
        else
        {
            if (IsLocked(instance, queueName))
                lockedQueue[instance].Remove(queueName);
        }
    }


    /// <summary>
    /// Executes a timed callback.
    /// </summary>
    private static IEnumerator ExecuteOnce(float timeToWait, YieldInstruction yieldToWait,
        Action callback, Action<ttHandler> callbackWithHandler)
    {
        // Wait until
        if (timeToWait > 0)
            yield return new WaitForSeconds(timeToWait);

        if (yieldToWait != null)
            yield return yieldToWait;

        // Executes the normal handler
        if (callback != null)
            callback();

        // Executes the callback with handler (and waits his yield)
        if (callbackWithHandler != null)
        {
            ttHandler t = new ttHandler();
            callbackWithHandler(t);

            if (t.yieldToWait != null)
                yield return t.yieldToWait;
        }

        yield return null;
    }


    /// <summary>
    /// Executes a callback inside a loop until time.
    /// </summary>
    private static IEnumerator ExecuteLoop(float duration, Action<ttHandler> callback)
    {
        // Only for positive values
        if (duration <= 0)
            yield break;

        ttHandler loopHandler = new ttHandler();

        // Run while active until duration
        while (loopHandler.isActive && loopHandler.timeSinceStart < duration)
        {
            // Custom delta time
            loopHandler.deltaTime = 1 / (duration - loopHandler.timeSinceStart) * Time.deltaTime;
            loopHandler.timeSinceStart += Time.deltaTime;

            // Execute
            if (callback != null)
                callback(loopHandler);

            // Yields once and resets
            if (loopHandler.yieldToWait != null)
            {
                yield return loopHandler.yieldToWait;
                loopHandler.yieldToWait = null;
            }

            yield return null;
        }
    }


    /// <summary>
    /// Executes a callback inside an infinite loop.
    /// </summary>
    private static IEnumerator ExecuteInfiniteLoop(Action<ttHandler> callback)
    {
        ttHandler loopHandler = new ttHandler();

        // Run while active
        while (loopHandler.isActive)
        {
            loopHandler.deltaTime = Time.deltaTime;
            loopHandler.timeSinceStart += Time.deltaTime;

            // Execute
            if (callback != null)
                callback(loopHandler);

            // Yields once and resets
            if (loopHandler.yieldToWait != null)
            {
                yield return loopHandler.yieldToWait;
                loopHandler.yieldToWait = null;
            }

            yield return null;
        }
    }
}