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
    //主线程
    private Thread          _mainThread;
    //参数
    private HTTPParamIndie  _param;
    //本地文件流
    private FileStream      _fileStream;
    //http流
    private Stream          _stream;
    //读取数据用多线程
    private Thread[]        _readThreads;
    //目标数据md5值
    private string          _md5;

    public HTTPDownloadIndie(HTTPParamIndie param)
    {
        SetParam(param);
    }

    private void SetParam(HTTPParamIndie param)
    {
        _param = param;
    }

    /// <summary>
    /// 开启下载
    /// </summary>
    public override void DownLoad() {
        // 开启线程下载
        if (_mainThread == null)
        {
            _mainThread              = new Thread(StartDownLoad);
            _mainThread.IsBackground = true;
        }
        _mainThread.Start();

        
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
        try {
            SetData();
            //md5校验
            if(!MD5Verfied())
            {
                _param.MD5FailCallBack?.Invoke();
                Error           = "MD5 is not verfied";
                return;
            }

            //数据已下载完毕
            if (_fileStream == null || _stream == null)
            {
                if (FileLength >= TotalLength)
                {
                    Progress    = 1;
                    Status      = 1;
                }
                return;
            }
            StartReadStream();

        } catch (Exception e) {
            if (_retriedTime < _param.RetryTime)
            {
                StartDownLoad();
                _retriedTime++;
                return;
            }
            Error   = e.Message;
            Status  = -1;
            _param.FailCallBack?.Invoke();
        }
    }

    /// <summary>
    /// 获取数据
    /// </summary>
    public override void SetData()
    {
        var datas   = HTTPRequest.Get(this._param);
        _fileStream = datas.Item1;
        _stream     = datas.Item2;
        FileLength  = datas.Item3;
        TotalLength = datas.Item4;
        RefreshProgress();
        _lastFileLength = FileLength;
    }

    /// <summary>
    /// 开始多线程读取数据流
    /// </summary>
    public void StartReadStream()
    {
        _readThreads = new Thread[_param.MaxThread];
        for(int i=0;i<_readThreads.Length;i++)
        {
            _readThreads[i] = new Thread(new ThreadStart(ReadStream));
            _readThreads[i].Start();
        }
    }

    /// <summary>
    /// 停止读取数据流
    /// </summary>
    public void StopReadStream()
    {
        for(int i=0;i<_readThreads.Length;i++)
        {
            _readThreads[i].Abort();
        }
        ResetData();
    }

    /// <summary>
    /// 重置数据
    /// </summary>
    public override void ResetData()
    {
        base.ResetData();

        _mainThread.Abort();
        _stream.Close();
        _stream.Dispose();

        _fileStream.Close();
        _fileStream.Dispose();

        if (_param.IsCalcSpeed)
        {
            _speedTimer.Stop();
            _speedTimer.Close();
            _speedTimer.Dispose();
        }

        _readThreads = null;
    }


    /// <summary>
    /// 单线程读取数据流
    /// </summary>
    private void ReadStream()
    {
        byte[] buff     = new byte[4096];
        int len         = -1;
        lock(this)
        {
            while ((len = _stream.Read(buff, 0, buff.Length)) > 0)
            {
                if (_isStop || Progress >=1)
                {
                    break;
                }
                _fileStream.Write(buff, 0, len);
                FileLength += len;
                RefreshProgress();
            }

            // 标记下载完成
            if (Progress >= 1)
            {
                Status = 1;
                _param.SuccessCallBack?.Invoke();
            }

            StopReadStream();
        }
    }

    /// <summary>
    /// 刷新进度
    /// </summary>
    private void RefreshProgress()
    {
        Progress = FileLength * 1.0f / TotalLength;
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

    /// <summary>
    /// 传入md5值与目标md5值是否一致
    /// </summary>
    /// <returns></returns>
    private bool MD5Verfied()
    {
        if (_param == null)
            return false;
        return true;
        //return _param.MD5.CompareTo(GetMD5Code()) == 0;
    }

    /// <summary>
    /// 生成目标md5值
    /// </summary>
    /// <returns></returns>
    private string GetMD5Code()
    {
        if (!string.IsNullOrEmpty(_md5))
            return _md5;

        if (_stream == null)
            return string.Empty;
      
        StringBuilder strBlder = new StringBuilder();
        using (MD5CryptoServiceProvider md5 = new MD5CryptoServiceProvider())
        {
            Byte[] hashes = md5.ComputeHash(_stream);
            for(int i=0;i<hashes.Length;i++)
            {
                strBlder.Append(hashes[i].ToString("X2"));
            }
        }
        _md5        = strBlder.ToString();
        return _md5;
    }
}