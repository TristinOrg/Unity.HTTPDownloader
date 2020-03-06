using System;
using System.Timers;
using System.IO;
using Timer = System.Timers.Timer;
using System.Threading;
using System.Security.Cryptography;
using System.Text;
/// <summary>
/// 文件下载
/// </summary>
public class HTTPDownloadIndie:HTTPDownloadBase
{
    //参数
    private HTTPParamIndie  _param;
    //目标数据md5值
    private string          _md5;

    public override long FileLength
    {
        get
        {
           return _param.FileLength;
        }
        set => base.FileLength = value;
    }

    public override long TotalLength
    {
        get
        {
            return _param.TotalLength;
        }
        set => base.TotalLength = value;
    }

    public override float Progress
    {
        get
        {
            return (FileLength * 1.0f / TotalLength) * 100;
        }
        set => base.Progress = value;
    }
    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="param"></param>
    public HTTPDownloadIndie(HTTPParamIndie param)
    {
        SetParam(param);
    }

    /// <summary>
    /// 单独设置参数
    /// </summary>
    /// <param name="param"></param>
    private void SetParam(HTTPParamIndie param)
    {
        _param = param;
        if (_param.IsCalcSpeed)
            HTTPTask.MaxThread = _param.MaxThread;
    }

    /// <summary>
    /// 开启下载
    /// </summary>
    public override void DownLoad() {

        StartDownLoad();

        if (!_param.IsCalcSpeed)
            return;

        //开始计时
        if (_speedTimer == null)
        {
            _speedTimer = new Timer();
            _speedTimer.Elapsed += CalcSpeed;
            _speedTimer.Interval = 1000;
        }
        _speedTimer.Start();      
    }

    /// <summary>
    /// 开始下载
    /// </summary>
    private void StartDownLoad() {
        _param.SuccessCallBack += ResetData;
        HTTPTask.AddTask(_param);
    }

    /// <summary>
    /// 重置数据
    /// </summary>
    public override void ResetData()
    {
        base.ResetData();

        if (_param.IsCalcSpeed)
        {
            _speedTimer.Stop();
            _speedTimer.Close();
            _speedTimer.Dispose();
        }
    }

    /// <summary>
    /// 计算下载速度的计时器
    /// </summary>
    /// <param name="source"></param>
    /// <param name="args"></param>
    public void CalcSpeed(object source,ElapsedEventArgs args)
    {
        if (FileLength <= _lastFileLength)
            return;

        long lengthDiffer   = FileLength - _lastFileLength;
        Speed               = lengthDiffer * 1.0f / 1024;
        _lastFileLength = FileLength;
    }
}