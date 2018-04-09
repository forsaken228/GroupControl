﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using GroupControl.Model;
using GroupControl.Common;
using System.Xml;
using System.Net.Sockets;
using System.Net;
using System.Management;

namespace GroupControl.Helper
{
    public class BaseAction
    {
        private IList<string> devices = new List<string>();

        public readonly static object _obj = new object();

        private readonly static object _threadObj = new object();

        private delegate void SetCallback<T>(Control control, T t, Action<Control, T> func);

        private Lazy<Dictionary<string, Control>> _dic;

        private Lazy<Dictionary<string, Queue<byte[]>>> _dicImageStream;

        private Lazy<Queue<Dictionary<string, EnumTaskState>>> _taskStateQueue;

        private Lazy<Dictionary<string, CheckBox>> _checkSomeEquipments;

        private Control _currentSameScreenControl;

        public delegate void TagTaskState(UpdateWithTaskStateViewModel viewModel);

        public TagTaskState _tagTaskState;

        public int _maxPort = 8000;

        /// <summary>
        /// 分辨率为720*1280 的手机  从坐标之间的差距  
        /// </summary>
        public int DValue = 96;

        /// <summary>
        /// 基准分辨率X
        /// </summary>
        private const int WM_X = 720;

        /// <summary>
        ///基准分辨率y
        /// </summary>
        private const int WM_Y = 1280;

        private Control _parentControl;

        /// <summary>
        ///根据不同的手机分辨率 换算出点击的点的坐标
        /// </summary>
        public Func<WMPoint, string, string> func = (wmPoint, deviceStr) =>
       {

           return string.Format("{0} {1} {2}", deviceStr, wmPoint.WM_X, wmPoint.WM_Y);

       };

        /// <summary>
        ///根据不同的手机分辨率 换算出点击的点的坐标
        /// </summary>
        public Func<WMPoint,WMPoint, string, string> swipFunc = (wmPoint1,wmPoint2, deviceStr) =>
        {

            return string.Format("{0} {1} {2} {3} {4} 2000", deviceStr, wmPoint1.WM_X, wmPoint1.WM_Y, wmPoint2.WM_X, wmPoint2.WM_Y);

        };

        public Action<string, int, int,WMPoint> actionDirectStr
        {
            get
            {
                return (device, x, y, wmPoint) =>
                {
                    var wmPointModel = ConvertPointWithDiffentWM(device, x, y, wmPoint);

                    InitProcessWithTaskState(device, func(wmPointModel, "shell input tap "));

                };
            }
        }

        public Action<string, int, int, int, int, WMPoint> actionDirectSwipStr
        {
            get
            {
                return (device, x, y, x1, y1, wmPoint) =>
                {
                    var wmPointModel1 = ConvertPointWithDiffentWM(device, x, y, wmPoint);
                    var wmPointModel2 = ConvertPointWithDiffentWM(device, x1, y1, wmPoint);

                    InitProcessWithTaskState(device, swipFunc(wmPointModel1, wmPointModel2, "shell input swipe"));

                };
            }
        }

        public Control ParentControl
        {
            get
            {
                return _parentControl;
            }

            set
            {
                _parentControl = value;
            }
        }

        public Control CurrentSameScreenControl
        {
            get
            {
                return _currentSameScreenControl;
            }

            set
            {
                _currentSameScreenControl = value;
            }
        }

        public IList<string> Devices
        {
            get
            {
                return devices;
            }

            set
            {
                devices = value;
            }
        }

        public Lazy<Dictionary<string, Queue<byte[]>>> DicImageStream
        {
            get
            {
                return _dicImageStream;
            }

            set
            {
                _dicImageStream = value;
            }
        }

        public Lazy<Dictionary<string, Control>> Dic
        {
            get
            {
                return _dic;
            }

            set
            {
                _dic = value;
            }
        }


        public Lazy<Dictionary<string, CheckBox>> CheckSomeEquipments
        {
            get
            {
                return _checkSomeEquipments;
            }

            set
            {
                _checkSomeEquipments = value;
            }
        }

        public int MaxPort
        {
            get
            {
                return _maxPort;
            }

            set
            {
                _maxPort = value;
            }
        }

        /// <summary>
        /// 记录每个任务中设备状态
        /// </summary>
        public Lazy<Queue<Dictionary<string, EnumTaskState>>> TaskStateQueue
        {
            get { return _taskStateQueue; }

            set { _taskStateQueue = value; }
        }

        public BaseAction()
        {
            _dic = new Lazy<Dictionary<string, Control>>();

            _dicImageStream = new Lazy<Dictionary<string, Queue<byte[]>>>();

            _taskStateQueue = new Lazy<Queue<Dictionary<string, EnumTaskState>>>();

            _checkSomeEquipments = new Lazy<Dictionary<string, CheckBox>>();
        }

        public void ShowAllCutImageFromQuery(Action<string> startAction, Action<string> endAction)
        {
            if (null != _dic && _dic.Value.Count > 0)
            {
                _dic.Value.Keys.ToList().ForEach((item) =>
                {
                    var port = ++_maxPort;

                    startAction?.Invoke(item);

                    SingleShowImageWithSocket(_dic.Value[item], port, (device) =>
                    {
                        endAction(device);

                    });

                });
            }
        }

        /// <summary>
        /// 获取手机的分辨率
        /// </summary>
        /// <param name="device"></param>
        /// <returns></returns>
        public WMPoint GetWMSize(string device)
        {
            var returnStrList = InitProcessWithTaskState(device, "shell wm size", true);
            if (returnStrList != null && returnStrList.Count > 0)
            {
                var returnStr = returnStrList.FirstOrDefault();
                Regex r = new Regex("\\d+\\.?\\d*");
                bool ismatch = r.IsMatch(returnStr);
                MatchCollection mc = r.Matches(returnStr);
                if (mc != null && mc.Count == 2)
                {
                    var castList = mc.Cast<Match>().ToList();
                    return new WMPoint() { WM_X = int.Parse(castList.First().Value), WM_Y = int.Parse(castList.Last().Value) };
                }
            }

            return default(WMPoint);
        }

        /// <summary>
        /// 不同屏幕分辨率中点击坐标的转换
        /// </summary>
        /// <param name="device"></param>
        /// <param name="x">点击的点的X坐标</param>
        /// <param name="y">点击的点的Y坐标</param>
        /// <param name="wmPoint">当前手机的分辨率</param>
        /// <returns></returns>
        public WMPoint ConvertPointWithDiffentWM(string device, int x, int y, WMPoint wmPoint = null)
        {
            if (wmPoint == null)
            {
                wmPoint = GetWMSize(device);
            }

            if (wmPoint != null)
            {
                return new WMPoint() { WM_X = wmPoint.WM_X * x / WM_X, WM_Y = wmPoint.WM_Y * y / WM_Y };
            }

            return new WMPoint() { WM_X = x, WM_Y = y };
        }

        /// <summary>
        /// 打开App
        /// </summary>
        /// <param name="device"></param>
        public void OpenApp(string device, string packageName, string directUIPageName, Action whileAction)
        {
            ///回到主界面
            InitProcessWithTaskState(device, " shell input keyevent 3");

            ///关闭app
            InitProcessWithTaskState(device, string.Format(" shell am force-stop {0}", packageName));

            //开启app
            InitProcessWithTaskState(device, string.Format("shell am start {0}", directUIPageName), true);

            whileAction();
        }

        /// <summary>
        /// 显示映射
        /// </summary>
        /// <param name="deviceStr"></param>
        /// <param name="picture"></param>
        public void SingleShowImageWithSocket(Control picture, int port = 8000, Action<string> action = null)
        {
            var currentDevice = picture.Name.Split('_')[0];

            var byteLen = 40960;

            var currentImageByte = new byte[0];

            var currentIndex = 0;

            var currentAllLen = 0;

            Task.Factory.StartNew<string>((obj) =>
            {

                var device = obj as string;

                try
                {
                    ///解锁
                    UnlockSingleScreen(device);

                    ///关闭投影服务
                    ///
                    InitProcess(device, " shell am force-stop com.example.testnewscreencap");

                    ///开启投影服务
                    ///
                    InitProcess(device,
                        "shell am start com.example.testnewscreencap/com.example.testnewscreencap.MainActivity", true);

                    ///是否正常启动投影服务
                    CircleDetection("testnewscreencap.MainActivity", device);

                    ///回到主界面
                    InitProcess(device, " shell input keyevent 3");

                    ///解锁
                    UnlockSingleScreen(device);

                    ///端口映射
                    InitProcess(device, string.Format("forward tcp:{0} tcp:9000", port));

                }
                catch (Exception)
                {

                }

                return device;

            }, currentDevice, TaskCreationOptions.LongRunning).ContinueWith((task) =>
             {

                 action?.Invoke(task.Result);


             }, TaskContinuationOptions.LongRunning).ContinueWith((task) =>
             {

                 try
                 {
                     SingleHepler<SocketHelper>.Instance.Reciver((currentByte, receiveNumber) =>
                     {

                         var len = 0;

                         int.TryParse(Encoding.UTF8.GetString(currentByte.Take(5).ToArray()), out len);

                         if (len == 0)
                         {
                             using (var partStream = new MemoryStream(currentByte))
                             {

                                 var surplusData = (currentAllLen - currentIndex) / byteLen;

                                 var receiveDataLen = surplusData == 0
                                     ? ((currentAllLen - currentIndex) % byteLen)
                                     : byteLen;

                                 partStream.Read(currentImageByte, currentIndex, receiveDataLen);

                                 partStream.Close();

                                 currentIndex += receiveDataLen;
                             }

                         }
                         else
                         {

                             currentImageByte = new byte[len];

                             currentAllLen = len;

                             currentIndex = receiveNumber - 5;

                             using (var partStream = new MemoryStream(currentByte.Skip(5).ToArray()))
                             {
                                 if (len <= byteLen)
                                 {
                                     partStream.Read(currentImageByte, 0, len);
                                 }
                                 else
                                 {
                                     partStream.Read(currentImageByte, 0, currentIndex);
                                 }

                                 partStream.Close();

                             }

                         }

                         ///接受完毕
                         if (currentAllLen <= currentIndex && currentAllLen > 0 && currentIndex > 0)
                         {
                             SingleShowImageToControl(currentImageByte, picture);

                             currentImageByte = new byte[0];

                             currentIndex = 0;

                             currentAllLen = 0;
                         }

                     }, port, byteLen);
                 }
                 catch (Exception e)
                 {
                     Console.WriteLine(e);

                     //throw;
                 }


             }, TaskContinuationOptions.LongRunning);


        }

        public void SingleShowImageToControl(Byte[] currentImageByte, Control currentControl)
        {

            using (var stream = new MemoryStream(currentImageByte))
            {

                try
                {

                    var currentControlList = new List<Control>() { currentControl };

                    if (null != _currentSameScreenControl && _currentSameScreenControl.Tag.Equals(currentControl.Name))
                    {
                        currentControlList.Add(_currentSameScreenControl);
                    }

                    if (null != currentControlList && currentControlList.Count > 0)
                    {
                        currentControlList.ToList().ForEach((item) =>
                        {
                            SingleHepler<BaseAction>.Instance.SetContentWithCurrentThread<MemoryStream>(item, stream,

                                (control, str) =>
                                {

                                    var pic = (control as Panel) == null ? (control as PictureBox) : control.Controls[1] as PictureBox;

                                    if (null != pic)
                                    {
                                        pic.Image = Bitmap.FromStream(str);

                                    }



                                });

                        });
                    }
                }
                catch (Exception ex)
                {

                    //  Console.WriteLine(ex.Message);

                }

                finally
                {
                    stream.Close();
                }
            }

        }

        public void SetContentWithCurrentThread<T>(Control control, T t, Action<Control, T> func)
        {
            try
            {
                if (control.InvokeRequired)//如果调用控件的线程和创建创建控件的线程不是同一个则为True
                {
                    while (!control.IsHandleCreated)
                    {

                        //解决窗体关闭时出现“访问已释放句柄“的异常
                        if (control.Disposing || control.IsDisposed)

                            return;

                    }

                    SetCallback<T> d = new SetCallback<T>(SetContentWithCurrentThread);

                    control.Invoke(d, new object[] { control, t, func });

                }
                else
                {

                    func(control, t);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);

                // throw;
            }
        }


        /// <summary>
        /// 根据任务状态判断是否调用进程
        /// </summary>
        /// <param name="device"></param>
        /// <param name="directStr"></param>
        /// <param name="isHaveOut"></param>
        /// <returns></returns>
        public IList<string> InitProcessWithTaskState(string device, string directStr, bool isHaveOut = false)
        {

            // 取消任务 或执行完毕 
            if (CheckTaskState(device) == EnumTaskState.Cancle || !devices.Contains(device))
            {
                throw new TaskCanceledException();
            }

            return InitProcess(device, directStr, isHaveOut);
        }

        public IList<string> InitProcess(string device, string directStr, bool isHaveOut = false)
        {
            try
            {

                // new process对象
                Process p = new System.Diagnostics.Process();
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.FileName = SingleHepler<ConfigInfo>.Instance.ADBExeFileUrl;
                // p.StartInfo.FileName = @"cmd.exe";
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.RedirectStandardInput = true;
                p.StartInfo.RedirectStandardOutput = true;  //重定向标准输出   
                p.StartInfo.CreateNoWindow = true;          //设置不显示窗口   
                p.StartInfo.Arguments = InitDirection(directStr, device);
                p.Start();

                // p.StandardInput.WriteLine(directStr);

                var list = new List<string>();

                if (isHaveOut)
                {
                    // p.StandardInput.WriteLine("exit");

                    var outStream = p.StandardOutput;

                    while (!outStream.EndOfStream)
                    {
                        list.Add(outStream.ReadLine());
                    }
                }

                p.WaitForExit();

                p.Close();

                return list;
            }
            catch (Exception ex)
            {

                throw;
            }

        }

        private string InitDirection(string direct, string device)
        {

            // Thread.Sleep(1000);

            if (string.IsNullOrEmpty(device))
            {
                return direct;
            }

            return string.Format(" -s {0}  {1} ", device, direct);
        }

        public void GetDevices(IList<DeviceToNickNameViewModel> devices, MouseEventHandler handler, Action<MouseEventHandler, DeviceToNickNameViewModel> action)
        {

            if (null != devices && devices.Count > 0)
            {

                try
                {
                    devices.Select(o =>
                    {

                        action(handler, o);

                        return o;

                    }).ToList();
                }
                catch (Exception ex)
                {

                    throw;
                }

            }
        }

        /// <summary>
        ///循环检测是否进入指定页面
        /// </summary>
        public bool CircleDetection(string pageName, string currentDevice, bool isCircle = true)
        {

            var stateStr = string.Empty;

            var validateIndex = 0;

            var isSuccess = false;

            //检测微信运行状态
            while (!stateStr.Contains(pageName) && validateIndex < 5)
            {

                if (isCircle == false)
                {
                    ////是否被移除
                    var isRemove = CheckEquipmentIsConnecting(currentDevice);

                    if (isRemove)
                    {
                        return false;
                    }
                }

                var list = InitProcessWithTaskState(currentDevice, " shell dumpsys window | grep mCurrentFocus", true);

                if (null == list || list.Count == 0)
                {
                    return false;
                }

                stateStr = string.Join("|", list);

                if (isCircle)
                {
                    validateIndex++;
                }

                if (stateStr.Contains(pageName))
                {
                    isSuccess = true;
                }

                Thread.Sleep(1000);

            }

            return isSuccess;
        }

        public void UnlockScreen(bool isLock = true)
        {
            if (null != devices && devices.Count > 0)
            {
                devices.ToList().ForEach((item) =>
                {

                    Task.Factory.StartNew((o) =>
                    {
                        var device = o as string;

                        var state = GetIsLock(device);

                        if (isLock && state)
                        {
                            //当前设备为锁屏状态
                            InitProcessWithTaskState(device, "shell input keyevent 26");
                        }
                        else if (!isLock && !state)
                        {
                            InitProcessWithTaskState(device, "shell input keyevent 26");
                            InitProcessWithTaskState(device, "shell input swipe 360 700 700 50");
                            InitProcessWithTaskState(device, "shell input swipe 50 500 700 500");
                        }

                    }, item, TaskCreationOptions.LongRunning);

                });
            }
        }


        public void UnlockSingleScreen(string device)
        {
            var state = GetIsLock(device);

            if (!state)
            {
                InitProcess(device, "shell input keyevent 26");
            }

            InitProcessWithTaskState(device, "shell input swipe 360 700 700 50");
            InitProcessWithTaskState(device, "shell input swipe 50 500 700 500");
        }

        public void CreateThreadAndStartTask(object obj, Action<object> action, string threadName = null)
        {

            Thread thr = new Thread(new ParameterizedThreadStart(action));

            thr.IsBackground = true;

            thr.Name = threadName;

            thr.Start(obj);

        }

        public WXActionViewModel CreatUIXML(WXActionViewModel viewModel, Func<WXActionViewModel, XmlDocument, WXActionViewModel> func)
        {
            viewModel.PullNativePath = string.Format(@"{0}\{1}", SingleHepler<ConfigInfo>.Instance.CutImageFileUrl, viewModel.Device);

            if (!Directory.Exists(viewModel.PullNativePath))
            {
                Directory.CreateDirectory(viewModel.PullNativePath);
            }

            ///生成当前页面的xml文档
            InitProcessWithTaskState(viewModel.Device, string.Format("shell uiautomator dump /sdcard/{0}.xml", viewModel.XMLName));

            //下载到本地电脑
            InitProcessWithTaskState(viewModel.Device, string.Format("pull /sdcard/{0}.xml {1}", viewModel.XMLName, viewModel.PullNativePath));

            var filePath = string.Format(@"{0}\{1}.xml", viewModel.PullNativePath, viewModel.XMLName);

            if (File.Exists(filePath))
            {
                XmlDocument doc = new XmlDocument();

                string path = filePath;

                doc.Load(path);

                #region 进行检测

                return func(viewModel, doc);

                #endregion

            }

            return viewModel;
        }

        public PictureBox GetPictureFromControl(Control panel)
        {

            var controls = panel.Controls;

            foreach (var child in controls)
            {

                var currentChild = child as PictureBox;

                if (null != currentChild)
                {
                    return currentChild;
                }
            }

            return default(PictureBox);
        }


        /// <summary>
        /// 判断是否锁屏状态
        /// </summary>
        /// <param name="device"></param>
        /// <returns></returns>
        public bool GetIsLock(string device)
        {
            var state = false;

            ///判断是否锁屏
            var list = InitProcess(device, "shell dumpsys window policy|grep mScreenOnFully", true);

            if (null != list && list.Count > 0)
            {
                state = list.FirstOrDefault().Contains("mScreenOnEarly=true");
            }

            return state;
        }


        /// <summary>
        /// 检测设备是否处于连接状态
        /// </summary>
        public bool CheckEquipmentIsConnecting(string device)
        {
            var returnDataList = InitProcessWithTaskState(device, " shell wm size", true);

            var isRemove = false;

            if (null == returnDataList || returnDataList.Count == 0)
            {
                isRemove = true;
            }

            ///为了
            else
            {
                var currentData = returnDataList.FirstOrDefault();

                if (currentData.Contains("error"))
                {
                    isRemove = true;
                }
            }

            return isRemove;
        }


        #region 导入通讯录

        #region 分配手机号到手机 导入通讯录

        public IList<Task> ImportContracts(ref IList<VCardViewModel> vCardViewModelList, IList<string> devices, Action<string> action)
        {
            var firstViewModel = vCardViewModelList.FirstOrDefault();

            var currentvCardList = vCardViewModelList;

            var taskList = new Lazy<List<Task>>();

            if (null != devices && devices.Count > 0)
            {
                var index = 0;

                Func<int, string, dynamic> _currentFunc = (currentIndex, device) =>
                 {
                     var skipIndex = currentIndex * firstViewModel.ImportCount;

                     var currentImportCount = (currentvCardList.Count - skipIndex) < firstViewModel.ImportCount ? (currentvCardList.Count - skipIndex) : firstViewModel.ImportCount;

                     var list = currentvCardList.Skip(skipIndex).Take(firstViewModel.ImportCount).Select(o =>
                      {

                          o.Device = device;

                          return o;

                      }).ToList();

                     return new { list = list, importCount = currentImportCount };
                 };

                devices.ToList().ForEach((item) =>
                {

                    var currenTask = Task.Factory.StartNew<VCardViewModel>((i) =>
                        {

                            var currentIndex = (int)i;

                            var dynamicData = _currentFunc.Invoke(currentIndex, item);

                            action(string.Format("正在生成与设备 {0} 相关的VCF文件", item));

                            CreateVCard(dynamicData.list);

                            return new VCardViewModel() { ImportCount = dynamicData.importCount, Device = item, Name = firstViewModel.Name };

                        }, index).ContinueWith<VCardViewModel>((task) =>
                        {

                            var returnData = task.Result;

                            action(string.Format("正在向设备 {0} 导入 {1} 条数据", item, returnData.ImportCount <= 0 ? 0 : returnData.ImportCount));

                            ///更新设备新朋友状态  导入的 通讯录时间 
                            if (returnData.ImportCount > 0)
                            {
                                Task.Factory.StartNew(() =>
                                {

                                    SingleHepler<GroupControl.BLL.NewFriendStateBLL>.Instance.SaveChange(new NewFriendStateViewModel() { IsImportContracts = true, IsHaveNewFriend = true, Device = returnData.Device });

                                });
                            }

                            UploadAndImportVCFtToPhone(returnData, (o) =>
                            {

                                action(o);

                            });

                            return returnData;

                        }).ContinueWith((task) =>
                        {

                            var returnData = task.Result;

                            var nativeFilePath = Path.Combine(SingleHepler<ConfigInfo>.Instance.ContractsFileUrl, returnData.Device, string.Format("{0}.vcf", returnData.Name));

                            ///删除本地 VCF文件
                            if (File.Exists(nativeFilePath))
                            {
                                File.Delete(nativeFilePath);
                            }

                        });

                    taskList.Value.Add(currenTask);

                    index++;

                });
            }

            return taskList.Value;

        }


        /// <summary>
        /// 单个设备导入通讯录
        /// </summary>
        /// <param name="viewModel"></param>
        /// <param name="dynamicData"></param>
        /// <param name="action"></param>
        /// <returns></returns>
        protected async Task SingleImportContracts(VCardViewModel viewModel, dynamic dynamicData, Action<string> action)
        {

            action(string.Format("正在生成与设备 {0} 相关的VCF文件", viewModel.Device));

            var currenTask = await Task.Factory.StartNew<VCardViewModel>(() =>
            {

                CreateVCard(dynamicData.List);

                return new VCardViewModel() { ImportCount = dynamicData.importCount, Device = viewModel.Device, Name = viewModel.Name };

            });

            action(string.Format("正在向设备 {0} 导入 {1} 条数据", viewModel.Device, currenTask.ImportCount <= 0 ? 0 : currenTask.ImportCount));

            currenTask = await Task.Factory.StartNew(() =>
              {
                  ///更新设备新朋友状态  导入的 通讯录时间 
                  if (currenTask.ImportCount > 0)
                  {
                      Task.Factory.StartNew(() =>
                      {

                          SingleHepler<GroupControl.BLL.NewFriendStateBLL>.Instance.SaveChange(new NewFriendStateViewModel() { IsImportContracts = true, IsHaveNewFriend = true, Device = currenTask.Device });

                      });
                  }

                  UploadAndImportVCFtToPhone(currenTask, (o) =>
                  {

                      action(o);

                  });

                  return currenTask;

              });

            await Task.Factory.StartNew(() =>
            {
                var returnData = currenTask;

                var nativeFilePath = Path.Combine(SingleHepler<ConfigInfo>.Instance.ContractsFileUrl, returnData.Device, string.Format("{0}.vcf", returnData.Name));

                ///删除本地 VCF文件
                if (File.Exists(nativeFilePath))
                {
                    File.Delete(nativeFilePath);
                }

            });
        }

        #endregion


        #region 读取文本文档
        public IList<VCardViewModel> GetPersonInfoFromTxt(VCardViewModel viewModel)
        {
            if (string.IsNullOrEmpty(viewModel.SourceFilePath))
            {
                return default(List<VCardViewModel>);
            }

            var extendStr = Path.GetExtension(viewModel.SourceFilePath);

            if (!extendStr.ToLower().Equals(".txt"))
            {
                return default(List<VCardViewModel>);
            }

            var contractsList = new List<VCardViewModel>();

            using (var fileStream = new FileStream(viewModel.SourceFilePath, FileMode.Open, FileAccess.Read))
            {
                using (var streamReader = new StreamReader(fileStream))
                {

                    while (!streamReader.EndOfStream)
                    {
                        var content = streamReader.ReadLine();

                        contractsList.Add(new VCardViewModel() { MobilePhone = content, Name = viewModel.Name, ImportCount = viewModel.ImportCount });
                    }

                    streamReader.Close();
                }

                fileStream.Close();
            }

            return contractsList;
        }

        #endregion

        #region 生成VCard

        /// <summary>
        /// 生成VCard
        /// </summary>
        public void CreateVCard(IList<VCardViewModel> list)
        {
            if (null == list || list.Count == 0)
            {
                return;
            }

            var viewModel = list.FirstOrDefault();

            var dir = Path.Combine(SingleHepler<ConfigInfo>.Instance.ContractsFileUrl, viewModel.Device);

            string fileName = Path.Combine(dir, string.Format("{0}.vcf", viewModel.Name));

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(fileName))
            {
                File.Delete(fileName);
            }

            using (var fileStream = new FileStream(fileName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
            {
                using (var stringWrite = new System.IO.StreamWriter(fileStream))
                {


                    list.ToList().ForEach((item) =>
                    {
                        stringWrite.WriteLine("BEGIN:VCARD");

                        stringWrite.WriteLine("VERSION:2.1");

                        stringWrite.WriteLine("FN:" + item.Name);

                        stringWrite.WriteLine("ORG;CHARSET=gb2312:" + item.Company);

                        stringWrite.WriteLine("TITLE:" + item.Job);

                        stringWrite.WriteLine("TEL;WORK;VOICE:" + item.WorkPhone);

                        stringWrite.WriteLine("TEL;CELL;VOICE:" + item.MobilePhone);

                        stringWrite.WriteLine("EMAIL;PREF;INTERNET:" + item.Email);

                        stringWrite.WriteLine("ADR;WORK:;;" + item.WorkAddress + ";;;" + string.Empty);

                        stringWrite.WriteLine("END:VCARD");

                    });


                    stringWrite.Close();
                }

                fileStream.Close();

            }

            viewModel.SourceFilePath = fileName;
        }


        #endregion

        #region 上传VCF文档  并且执行导入

        public void UploadAndImportVCFtToPhone(VCardViewModel viewModel, Action<string> action)
        {

            ///获取屏幕分辨率
            WMPoint wmPoint = GetWMSize(viewModel.Device);

            var nativeFilePath = Path.Combine(SingleHepler<ConfigInfo>.Instance.ContractsFileUrl, viewModel.Device, string.Format("{0}.vcf", viewModel.Name));

            ///清空手机上的通讯录
            InitProcessWithTaskState(viewModel.Device, "shell pm clear com.android.providers.contacts");

            ///删除VCF文件
            InitProcessWithTaskState(viewModel.Device, "shell rm /sdcard/contacts.vcf");

            ///上传VCF文件到手机 adb -s emulator-5554 push contacts.vcf /sdcard/contacts.vcf  
            InitProcessWithTaskState(viewModel.Device, string.Format("push {0} /sdcard/contacts.vcf", nativeFilePath));

            UnlockSingleScreen(viewModel.Device);

            ///关闭微信
            InitProcessWithTaskState(viewModel.Device, " shell am force-stop com.tencent.mm");

            ///从文件中, 将联系人import到android通讯录中, 导入过程耗时依联系人数量而定.  
            InitProcessWithTaskState(viewModel.Device, "shell am start -t 'text/x-vcard' -d 'file:///sdcard/contacts.vcf' -a android.intent.action.VIEW com.android.contacts 6");

            var isSuccess = CircleDetection("ImportVCardActivity", viewModel.Device, false);

            if (isSuccess)
            {
                actionDirectStr(viewModel.Device, 600, 1200, wmPoint);

                // InitProcessWithTaskState(viewModel.Device, "shell input tap  600 1200");
            }


            action(string.Format("设备 {0} 成功导入 {1} 条数据", viewModel.Device, viewModel.ImportCount > 0 ? viewModel.ImportCount : 0));
        }

        #endregion


        #endregion


        #region 执行任务 关注任务状态

        /// <summary>
        /// 检测任务状态
        /// </summary>
        /// <param name="func"></param>
        public void InitEquipmentWithTaskState(Func<Dictionary<string, EnumTaskState>, Dictionary<string, EnumTaskState>> func)
        {
            var dic = new Dictionary<string, EnumTaskState>();

            dic = func(dic);

            TaskStateQueue.Value.Enqueue(dic);
        }

        /// <summary>
        /// 批量操作 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="devices"></param>
        /// <param name="action"></param>
        /// <returns></returns>
        public IList<Task> CommonAction<T>(IList<string> devices, Action<T> action) where T : BaseViewModel, new()
        {
            var currentDevices = this.Devices;

            if (null != devices && devices.Count > 0)
            {
                currentDevices = devices;
            }

            var taskArray = new List<Task>();

            if (null != currentDevices && currentDevices.Count > 0)
            {
                InitEquipmentWithTaskState((dic) =>
                {
                    var currentDic = dic;

                    currentDevices.ToList().ForEach((item) =>
                    {
                        var task = MonitorTask<T>(action, new T() { Device = item });

                        taskArray.Add(task);

                        currentDic.Add(item, EnumTaskState.Start);

                    });

                    return dic;

                });
            }

            return taskArray;
        }


        /// <summary>
        /// 开启任务
        /// </summary>
        /// <param name="currenAction"></param>
        /// <param name="viewModel"></param>
        /// <returns></returns>
        public Task MonitorTask<T>(Action<T> currenAction, T viewModel) where T : BaseViewModel
        {
            var tokenSource = new CancellationTokenSource();

            var token = tokenSource.Token;

            var currentViewModel = new UpdateWithTaskStateViewModel()
            {
                EquipmentDevice = viewModel.Device,
                TaskStatus = EnumTaskState.Start
            };

            _tagTaskState(currentViewModel);

            var task = Task.Factory.StartNew(() =>
            {
                try
                {
                    currenAction(viewModel);
                }
                catch (Exception ex)
                {
                    //利用委托来记录错误信息
                }

            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Current).ContinueWith((returnTask) =>
               {
                   currentViewModel.TaskStatus = EnumTaskState.Complete;

                   //更改队列中任务状态
                   SingleSetTaskState(currentViewModel.EquipmentDevice, EnumTaskState.Complete);

                   ///更新UI
                   _tagTaskState(currentViewModel);

               }, token, TaskContinuationOptions.LongRunning, TaskScheduler.Current);

            ///任务取消的回掉事件
            token.Register(() =>
            {
                currentViewModel.TaskStatus = EnumTaskState.Cancle;

                _tagTaskState(currentViewModel);

            });

            return task;
        }

        #endregion

        #region 取消任务

        public EnumTaskState SingleSetTaskState(string device, EnumTaskState enumState = EnumTaskState.Cancle)
        {

            return ValidateData((o, p) =>
            {
                if (!o.ContainsKey(p))
                {
                    return EnumTaskState.Cancle;
                }

                lock (_obj)
                {
                    if (o.ContainsKey(p))
                    {
                        o[p] = enumState;
                    }
                }

                return enumState;

            }, device);
        }

        public void RemoveEquipmentStateFromQueue()
        {
            if (null != _taskStateQueue && _taskStateQueue.Value.Count > 0)
            {
                lock (_obj)
                {
                    if (null != _taskStateQueue && _taskStateQueue.Value.Count > 0)
                    {
                        _taskStateQueue.Value.Dequeue();
                    }
                }
            }
        }

        /// <summary>
        ///检测当前设备任务状态
        /// </summary>
        public EnumTaskState CheckTaskState(string device)
        {
            return ValidateData((o, p) =>
            {
                if (o.ContainsKey(p))
                {
                    return o[p];
                }

                return EnumTaskState.Cancle;

            }, device);

        }

        public EnumTaskState ValidateData(Func<Dictionary<string, EnumTaskState>, string, EnumTaskState> action, string device)
        {
            if (null == _taskStateQueue || _taskStateQueue.Value.Count == 0)
            {
                return EnumTaskState.Complete;
            }

            var dic = _taskStateQueue.Value.Peek();

            if (null == dic || dic.Count == 0)
            {
                return EnumTaskState.Complete;
            }

            return action(dic, device);
        }

        #endregion


        #region 获取本机MAC地址

        public string GetMacAddress()
        {
            try
            {
                var st = string.Empty;

                ManagementClass mc = new ManagementClass("Win32_ComputerSystem");

                ManagementObjectCollection moc = mc.GetInstances();

                if (null == moc || moc.Count == 0)
                {
                    return default(string);
                }

                foreach (ManagementObject mo in moc)
                {
                    st = mo["UserName"].ToString();
                }

                return st;
            }
            catch
            {
                return default(string);
            }
            finally
            {

            }
        }

        #endregion


    }

    public class WMPoint
    {
        public int WM_X { get; set; }

        public int WM_Y { get; set; }
    }
}