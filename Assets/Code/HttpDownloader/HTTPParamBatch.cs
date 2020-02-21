using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

public class HTTPParamBatch : HTTPParamBase
{
    public List<string> Urls    { get; set; }
    public List<string> Paths   { get; set; }
    public List<string> MD5S    { get; set; }

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="urls"></param>
    /// <param name="paths"></param>
    /// <param name="md5s"></param>
    /// <param name="maxThread"></param>
    /// <param name="timeOut"></param>
    /// <param name="retryTime"></param>
    /// <param name="successCallBack"></param>
    /// <param name="failCallBack"></param>
    /// <param name="md5FailCallBack"></param>
    public HTTPParamBatch(List<string> urls = null, List<string> paths = null, List<string> md5s = null,
                          int maxThread = 1, int timeOut = 100000, int retryTime = 0,
                          Action successCallBack = null, Action failCallBack = null,
                          Action md5FailCallBack = null)
    {
        SetData(urls,paths,md5s);
    }

    /// <summary>
    /// 单独设置参数
    /// </summary>
    /// <param name="urls"></param>
    /// <param name="paths"></param>
    /// <param name="md5s"></param>
    /// <param name="maxThread"></param>
    /// <param name="timeOut"></param>
    /// <param name="retryTime"></param>
    /// <param name="successCallBack"></param>
    /// <param name="failCallBack"></param>
    /// <param name="md5FailCallBack"></param>
    public void SetData(List<string> urls = null,List<string> paths = null, List<string> md5s = null,
                        int maxThread = 1, int timeOut = 100000,int retryTime = 0,
                        Action successCallBack = null, Action failCallBack = null,
                        Action md5FailCallBack = null)
    {
        this.Urls               = urls;
        this.Paths              = paths;
        this.MD5S               = md5s;
        this.MaxThread          = maxThread;
        this.TimeOut            = timeOut;
        this.RetryTime          = retryTime;
        this.SuccessCallBack    = successCallBack;
        this.FailCallBack       = failCallBack;
        this.MD5FailCallBack    = md5FailCallBack;
        this.IsCalcSpeed        = false;
    }
}
