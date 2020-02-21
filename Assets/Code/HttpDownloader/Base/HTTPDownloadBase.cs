using System;
using System.Timers;
using System.IO;
using System.Threading;
using Timer = System.Timers.Timer;
using UnityEngine;

public class HTTPDownloadBase : MonoBehaviour
{
    // 下载进度
    public virtual float Progress
    {
        get;
        set;
    }

    //下载速度
    public virtual float Speed
    {
        get;
        set;
    }

    // 状态 0 正在下载 1 下载完成 -1 下载出错
    public virtual int Status
    {
        get;
        set;
    }
    // 错误信息
    public virtual string Error
    {
        get;
        set;
    }
    //当前长度
    public virtual long FileLength
    {
        get;
        set;
    }

    // 总长度
    public virtual long TotalLength
    {
        get;
        set;
    }
    //已重试次数
    protected int         _retriedTime;
    //计算下载速度计时器
    protected Timer       _speedTimer;
    //上个文件长度
    protected long        _lastFileLength;
    // 子线程是否停止标志
    protected bool        _isStop;
    //开始下载
    public virtual void     DownLoad() { }
    //获取数据
    public virtual void     SetData() { }
    //关闭下载
    public virtual void     Close() { }
    //重置数据
    public virtual void     ResetData()
    {
        Progress        = 0;
        Speed           = 0;
        Status          = 0;
        FileLength      = 0;
        TotalLength     = 0;
        _lastFileLength = 0;
        _isStop         = false;
        Error           = string.Empty;
    }
}
