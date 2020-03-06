using UnityEngine;
using System.Collections;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System;

/// <summary>
/// http下载器任务代码
/// </summary>
public static class HTTPTask
{
    //最大线程数
    private static int _maxThread;
    public static int MaxThread
    {
        get
        {
            return _maxThread;
        }
        set
        {
            if (value <= _maxThread)
                return;
            _maxThread = value;
        }
    }

    private static List<Thread>          _lstThread     = new List<Thread>();
    private static Queue<HTTPParamIndie> _queueParams   = new Queue<HTTPParamIndie>(); 

    /// <summary>
    /// 添加任务
    /// </summary>
    /// <param name="param"></param>
    public static void AddTask(HTTPParamIndie param)
    {
        StartTask(param);
    }

    /// <summary>
    /// 开始任务
    /// </summary>
    /// <param name="param"></param>
    private static void StartTask(HTTPParamIndie param)
    {
        if (_lstThread.Count < MaxThread)
        {
            Thread t            = new Thread(new ParameterizedThreadStart(TaskRun));
            param.RelateThread  = t;

            t.Start(param);
            _lstThread.Add(t);
        }
        else
        {
            _queueParams.Enqueue(param);
        }
    }

    /// <summary>
    /// 任务进行
    /// </summary>
    /// <param name="obj"></param>
    private static void TaskRun(object obj)
    {
        HTTPParamIndie param = obj as HTTPParamIndie;
        try
        {
            var datas               = HTTPRequest.Get(param);
            FileStream fileStream   = datas.Item1;
            Stream stream           = datas.Item2;
            long fileLength         = datas.Item3;
            long totalLength        = datas.Item4;

            param.FileLength    = fileLength;
            param.TotalLength   = totalLength;

            if (fileStream == null || stream == null)
            {
                StopTask(param);
                return;
            }

            if (!HTTPRequest.MD5Verfied(param.MD5, stream))
            {
                param.MD5FailCallBack?.Invoke();
                return;
            }

            byte[] buff = new byte[4096];
            int len = -1;
            while ((len = stream.Read(buff, 0, buff.Length)) > 0)
            {
                fileStream.Write(buff, 0, len);
                param.FileLength += len;
            }

            param.SuccessCallBack?.Invoke();

            StopTask(param);
            ContinueTask();
        }
        catch (Exception e)
        {
            if (param.CurRetryTime < param.RetryTime)
            {
                StopTask(param);
                StartTask(param);
                param.CurRetryTime++;
            }
            param.FailCallBack?.Invoke();
        }
    }

    /// <summary>
    /// 停止任务，并检测下一个任务
    /// </summary>
    /// <param name="param"></param>
    private static void StopTask(HTTPParamIndie param)
    {
        _lstThread.Remove(param.RelateThread);
        ContinueTask();
        param.RelateThread.Abort();
    }

    /// <summary>
    /// 如果还有任务，继续下载
    /// </summary>
    private static void ContinueTask()
    {
        if (_queueParams.Count > 0)
            StartTask(_queueParams.Dequeue());
        else
        {
            AbortAll();
        }
    }

    /// <summary>
    /// 停止所有任务
    /// </summary>
    public static void AbortAll()
    {
        for (int i = 0; i < _lstThread.Count; i++)
        {
            _lstThread[i].Abort();
        }
        _lstThread.Clear();
    }
}
