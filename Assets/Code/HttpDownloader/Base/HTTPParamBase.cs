using UnityEngine;
using System.Collections;
using System;
using System.IO;
using System.Threading;

public class HTTPParamBase
{
    //最大线程数
    public int      MaxThread       { get; set; }
    //超时时间
    public int      TimeOut         { get; set; }
    //重试次数
    public int      RetryTime       { get; set; }
    //当前已重试次数
    public int      CurRetryTime    { get; set; }
    //是否计算下载速度
    public bool     IsCalcSpeed     { get; set; }
    //相关线程
    public Thread   RelateThread    { get; set; }
    //当前文件大小
    public long     FileLength      { get; set; }
    //总文件大小
    public long     TotalLength     { get; set; }
    //下载成功回调
    public Action   SuccessCallBack;
    //下载失败回调
    public Action   FailCallBack;
    //md5检验失败回调
    public Action   MD5FailCallBack;
}
