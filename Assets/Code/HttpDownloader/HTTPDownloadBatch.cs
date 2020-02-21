using System;
using System.Timers;
using System.IO;
using Timer = System.Timers.Timer;
using System.Threading;
using System.Collections.Generic;

public class HTTPDownloadBatch : HTTPDownloadBase
{
    //下载器列表
    private List<HTTPDownloadIndie> _dlders = new List<HTTPDownloadIndie>();
    //参数
    private HTTPParamBatch          _param;
    //批量下载进度
    public override float Progress
    {
        get
        {
            float prgs = 0;
            TraverseDlder((dlder) =>
            {
                prgs += dlder.Progress;
            });
            return prgs *1.0f / _param.Urls.Count;
        }
    }

    //已下载个数
    public int DownloadedCount
    {
        get
        {
            int compCnt = 0;
            TraverseDlder((dlder) =>
            {
                if (dlder.Progress >= 1)
                    compCnt++;
            });
            return compCnt;
        }
    }

    //剩余个数
    public int LeftDownloadCount
    {
        get
        {
            return _param.Urls.Count - DownloadedCount;
        }
    }

    //批量下载状态
    public override int Status
    {
        get
        {
            float progress = Progress;
            if (progress >= 1)
                return 1;
            else
                return 0;
        }
    }

    //批量下载错误
    public override string Error
    {
        get
        {
            for (int i = 0; i < _dlders.Count; i++)
                if (!String.IsNullOrEmpty(_dlders[i].Error))
                    return _dlders[i].Error;
            return string.Empty;
        }
    }

    //批量下载当前数据大小
    public override long FileLength
    {
        get
        {
            long fileLengthSum = 0;
            TraverseDlder((dlder)=>
            {
                fileLengthSum += dlder.FileLength;
            });
            return fileLengthSum;
        }
    }

    //批量下载总数据大小
    private long _totalLength;
    public override long TotalLength
    {
        get
        {
            if(_totalLength == 0)
            {
                TraverseDlder((dlder) =>
                {
                    _totalLength += dlder.TotalLength;
                });
            }
            return _totalLength;
        }
    }

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="param"></param>
    public HTTPDownloadBatch(HTTPParamBatch param)
    {
        SetParam(param);
    }

    /// <summary>
    /// 单独设置参数
    /// </summary>
    /// <param name="param"></param>
    public void SetParam(HTTPParamBatch param)
    {
        _param = param;
    }

    /// <summary>
    /// 开始下载
    /// </summary>
    public override void DownLoad()
    {
        if (_param == null)
            return;
        SetDlders();
        SetSpeedTimer();
    }

    /// <summary>
    /// 初始化下载器
    /// </summary>
    private void SetDlders()
    {
        _dlders.Clear();
        int urlCnt          = _param.Urls.Count;
        int indieMaxThread  = _param.MaxThread / urlCnt;
        indieMaxThread      = Math.Max(indieMaxThread,1);
        for(int i=0;i< urlCnt;i++)
        {
            string url  = _param.Urls[i];
            string path = _param.Paths[i];
            string md5  = _param.MD5S[i];

            HTTPParamIndie indieParam   = new HTTPParamIndie(url,path,md5,indieMaxThread);
            indieParam.TimeOut          = _param.TimeOut;
            indieParam.RetryTime        = _param.RetryTime;

            HTTPDownloadIndie dlder     = new HTTPDownloadIndie(indieParam);
            _dlders.Add(dlder);

            dlder.DownLoad();
        }
    }

    /// <summary>
    /// 计算速度用的计时器
    /// </summary>
    private void SetSpeedTimer()
    {
       if(_speedTimer == null)
        {
            _speedTimer = new Timer();
            _speedTimer.Elapsed += CalcSpeed;
            _speedTimer.Interval = 1000;
        }
        _speedTimer.Stop();
        _speedTimer.Start();
    }

    /// <summary>
    /// 遍历下载器
    /// </summary>
    /// <param name="action"></param>
    private void TraverseDlder(Action<HTTPDownloadIndie> action)
    {
        if (action == null)
            return;
        for(int i=0;i<_dlders.Count;i++)
        {
            action.Invoke(_dlders[i]);
        }
    }

    /// <summary>
    /// 计算下载总速度
    /// </summary>
    /// <param name="source"></param>
    /// <param name="arg"></param>
    private void CalcSpeed(object source,ElapsedEventArgs arg)
    {
        float speed = 0;
        TraverseDlder((dlder)=>
        {
            dlder.CalcSpeed(source,arg);
            speed += dlder.Speed;
        });
        Speed = speed;
        if (Progress >= 1)
            ResetData();
    }

    /// <summary>
    /// 重置数据
    /// </summary>
    public override void ResetData()
    {
        base.ResetData();
        TraverseDlder((dlder) =>
        {
            dlder.ResetData();
        });
        _speedTimer.Stop();
        _speedTimer.Close();
        _speedTimer.Dispose();
        _dlders.Clear();
    }
}
