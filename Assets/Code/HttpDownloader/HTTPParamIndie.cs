using UnityEngine;
using System.Collections;
using System;
/// <summary>
/// HTTP下载参数
/// </summary>
public class HTTPParamIndie : HTTPParamBase
{
    //下载地址
    public string   Url         { get; set; }
    //保存路径
    public string   Path        { get; set; }
    //md5值
    public string   MD5         { get; set; }

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="url"></param>
    /// <param name="path"></param>
    /// <param name="md5"></param>
    /// <param name="maxThread"></param>
    /// <param name="timeOut"></param>
    /// <param name="retryTime"></param>
    /// <param name="successCallBack"></param>
    /// <param name="failCallBack"></param>
    /// <param name="md5FailCallBack"></param>
    public HTTPParamIndie(string url = null,string path = null,string md5 = null,int maxThread =1,int timeOut=100000,
                    int retryTime=0,Action successCallBack =null,Action failCallBack = null,
                    Action md5FailCallBack = null)
    {
        SetData(url,path,md5,maxThread,timeOut,retryTime,successCallBack,failCallBack,md5FailCallBack);
    }


    /// <summary>
    /// 单独设置参数
    /// </summary>
    /// <param name="url"></param>
    /// <param name="path"></param>
    /// <param name="md5"></param>
    /// <param name="maxThread"></param>
    /// <param name="timeOut"></param>
    /// <param name="retryTime"></param>
    /// <param name="successCallBack"></param>
    /// <param name="failCallBack"></param>
    /// <param name="md5FailCallBack"></param>
    public void SetData(string url = null, string path = null, string md5 = null, int maxThread = 1, int timeOut = 100000,
                    int retryTime = 0, Action successCallBack = null, Action failCallBack = null,
                    Action md5FailCallBack = null)
    {
        this.Url                = url;
        this.Path               = path;
        this.MD5                = md5;
        this.MaxThread          = maxThread;
        this.TimeOut            = timeOut;
        this.RetryTime          = retryTime;
        this.SuccessCallBack    = successCallBack;
        this.FailCallBack       = failCallBack;
        this.MD5FailCallBack    = md5FailCallBack;
        this.IsCalcSpeed        = true;
    }
}
