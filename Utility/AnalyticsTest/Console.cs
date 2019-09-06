using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Consolation
{
    /// <summary>
    /// A console to display Unity's debug logs in-game.
    /// 运行时显示的调试信息输出界面
    /// </summary>
    class Console : MonoBehaviour
    {
        public static Version version = new Version(1, 0, 0);   //版本
        #region Inspector Settings
        /// <summary>
        /// The hotkey to show and hide the console window.
        /// </summary>
        public KeyCode toggleKey = KeyCode.BackQuote;   //显示隐藏热键，可在检视视图再设置
        /// <summary>
        /// Whether to open as soon as the game starts.
        /// </summary>
        public bool openOnStart = false;    //是否启动显示
        /// <summary>
        /// Whether to open the window by shaking the device (mobile-only).
        /// </summary>
        public bool shakeToOpen = true;
        /// <summary>
        /// The (squared) acceleration above which the window should open.
        /// </summary>
        public float shakeAcceleration = 3f;
        /// <summary>
        /// Whether to only keep a certain number of logs, useful if memory usage is a concern.
        /// </summary>
        public bool restrictLogCount = false;
        /// <summary>
        /// Number of logs to keep before removing old ones.
        /// 最大日志数
        /// </summary>
        public int maxLogCount = 1000;
        #endregion
        static readonly GUIContent clearLabel = new GUIContent("Clear", "Clear the contents of the console.");
        static readonly GUIContent collapseLabel = new GUIContent("Collapse", "Hide repeated messages.");
        const int margin = 20;
        const string windowTitle = "Console";   //窗口标题
        //Log等级类型
        static readonly Dictionary<LogType, Color> logTypeColors = new Dictionary<LogType, Color>{
            { LogType.Assert, Color.white },
            { LogType.Error, Color.red },
            { LogType.Exception, Color.red },
            { LogType.Log, Color.white },
            { LogType.Warning, Color.yellow },
        };
        bool isCollapsed; //控制是否重复的折叠
        bool isVisible; //是否显示
        readonly List<Log> logs = new List<Log>();
        readonly ConcurrentQueue<Log> queuedLogs = new ConcurrentQueue<Log>();
        Vector2 scrollPosition;
        readonly Rect titleBarRect = new Rect(0, 0, 10000, 20); //标题栏区域大小
        Rect windowRect = new Rect(margin, margin, Screen.width - (margin * 2), Screen.height - (margin * 2)); //窗口区域大小
        //日志过滤类型，控制哪些日志类型显示
        readonly Dictionary<LogType, bool> logTypeFilters = new Dictionary<LogType, bool>{
            { LogType.Assert, true },
            { LogType.Error, true },
            { LogType.Exception, true },
            { LogType.Log, true },
            { LogType.Warning, true },
        };
        #region MonoBehaviour Messages
        void OnDisable()
        {
            Application.logMessageReceivedThreaded -= HandleLogThreaded;
        }
        void OnEnable()
        {
            Application.logMessageReceivedThreaded += HandleLogThreaded;
        }
        void OnGUI()
        {
            if (!isVisible)
            {
                return;
            }
            //绘制可拖拽窗口
            windowRect = GUILayout.Window(123456, windowRect, DrawWindow, windowTitle);
        }
        void Start()
        {
            if (openOnStart)
            {
                isVisible = true;
            }
        }
        void Update()
        {
            UpdateQueuedLogs();
            if (Input.GetKeyDown(toggleKey))
            {
                isVisible = !isVisible;
            }
            //控制在移动设备上显示
            if (shakeToOpen && Input.acceleration.sqrMagnitude > shakeAcceleration)
            {
                isVisible = true;
            }
        } 
        //显示日志信息和打印次数
        #endregion
        void DrawCollapsedLog(Log log)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(log.GetTruncatedMessage());
            GUILayout.FlexibleSpace();
            GUILayout.Label(log.count.ToString(), GUI.skin.box);
            GUILayout.EndHorizontal();
        }
        //只显示日志信息
        void DrawExpandedLog(Log log)
        {
            for (var i = 0; i < log.count; i += 1)
            {
                GUILayout.Label(log.GetTruncatedMessage());
            }
        }
        void DrawLog(Log log)
        {
            GUI.contentColor = logTypeColors[log.type];
            if (isCollapsed)
            {
                DrawCollapsedLog(log);
            }
            else
            {
                DrawExpandedLog(log);
            }
        }
        //绘制日志列表
        void DrawLogList()
        {
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            // Used to determine height of accumulated log labels.
            GUILayout.BeginVertical();
            var visibleLogs = logs.Where(IsLogVisible);
            foreach (Log log in visibleLogs)
            {
                DrawLog(log);
            }
            GUILayout.EndVertical();
            var innerScrollRect = GUILayoutUtility.GetLastRect();
            GUILayout.EndScrollView();
            var outerScrollRect = GUILayoutUtility.GetLastRect();
            // If we're scrolled to bottom now, guarantee that it continues to be in next cycle.
            if (Event.current.type == EventType.Repaint && IsScrolledToBottom(innerScrollRect, outerScrollRect))
            {
                ScrollToBottom();
            }
            // Ensure GUI colour is reset before drawing other components.
            GUI.contentColor = Color.white;
        }
        //绘制右下方工具条
        void DrawToolbar()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(clearLabel))
            {
                logs.Clear();
            }
            foreach (LogType logType in Enum.GetValues(typeof(LogType)))
            {
                var currentState = logTypeFilters[logType];
                var label = logType.ToString();
                logTypeFilters[logType] = GUILayout.Toggle(currentState, label, GUILayout.ExpandWidth(false));
                GUILayout.Space(20);
            }
            isCollapsed = GUILayout.Toggle(isCollapsed, collapseLabel, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
        }
        void DrawWindow(int windowID)
        {
            DrawLogList();
            DrawToolbar();
            // Allow the window to be dragged by its title bar.
            GUI.DragWindow(titleBarRect);
        }
        Log? GetLastLog()
        {
            if (logs.Count == 0)
            {
                return null;
            }
            return logs.Last();
        }
        /// <summary>
        /// 多线程处理日志
        /// </summary>
        void UpdateQueuedLogs()
        {
            Log log;
            while (queuedLogs.TryDequeue(out log))
            {
                ProcessLogItem(log);
            }
        }
        void HandleLogThreaded(string message, string stackTrace, LogType type)
        {
            //创建日志对象入队，后面在主线程处理
            var log = new Log
            {
                count = 1,
                message = message,
                stackTrace = stackTrace,
                type = type,
            };
            
            // Queue the log into a ConcurrentQueue to be processed later in the Unity main thread,
            // so that we don't get GUI-related errors for logs coming from other threads
            queuedLogs.Enqueue(log);
        }
        void ProcessLogItem(Log log)
        {
            var lastLog = GetLastLog(); //获得列表最末的日志
            var isDuplicateOfLastLog = lastLog.HasValue && log.Equals(lastLog.Value);
            if (isDuplicateOfLastLog)
            {
                //重复日志记录增加该记录个数
                // Replace previous log with incremented count instead of adding a new one.
                log.count = lastLog.Value.count + 1;
                logs[logs.Count - 1] = log;
            }
            else
            {
                //否则添加进日志列表
                logs.Add(log);
                TrimExcessLogs();
            }
        }
        bool IsLogVisible(Log log)
        {
            return logTypeFilters[log.type];
        }
        /// <summary>
        /// 是否滚动到底部
        /// </summary>
        /// <param name="innerScrollRect"></param>
        /// <param name="outerScrollRect"></param>
        /// <returns></returns>
        bool IsScrolledToBottom(Rect innerScrollRect, Rect outerScrollRect)
        {
            var innerScrollHeight = innerScrollRect.height;
            // Take into account extra padding added to the scroll container.
            var outerScrollHeight = outerScrollRect.height - GUI.skin.box.padding.vertical;
            // If contents of scroll view haven't exceeded outer container, treat it as scrolled to bottom.
            if (outerScrollHeight > innerScrollHeight)
            {
                return true;
            }
            // Scrolled to bottom (with error margin for float math)
            return Mathf.Approximately(innerScrollHeight, scrollPosition.y + outerScrollHeight);
        }
        void ScrollToBottom()
        {
            scrollPosition = new Vector2(0, Int32.MaxValue);
        }
        void TrimExcessLogs()
        {
            if (!restrictLogCount)
            {
                return;
            }
            //如果日志超出最大日志数量，删除旧的日志记录
            var amountToRemove = logs.Count - maxLogCount;
            if (amountToRemove <= 0)
            {
                return;
            }
            logs.RemoveRange(0, amountToRemove);
        }
    }
    /// <summary>
    /// 日志信息结构体
    /// A basic container for log details.
    /// </summary>
    struct Log
    {
        public int count;
        public string message;
        public string stackTrace;
        public LogType type;
        /// <summary>
        /// The max string length supported by UnityEngine.GUILayout.Label without triggering this error:
        /// "String too long for TextMeshGenerator. Cutting off characters."
        /// </summary>
        private const int MaxMessageLength = 16382; //字符串显示最大数
        public bool Equals(Log log)
        {
            return message == log.message && stackTrace == log.stackTrace && type == log.type;
        }
        /// <summary>
        /// Return a truncated message if it exceeds the max message length.
        /// </summary>
        public string GetTruncatedMessage()
        {
            if (string.IsNullOrEmpty(message))
            {
                return message;
            }
            return message.Length <= MaxMessageLength ? message : message.Substring(0, MaxMessageLength);
        }
    }

    //小型的多线程安全队列
    /// <summary>
    /// Alternative to System.Collections.Concurrent.ConcurrentQueue
    /// (It's only available in .NET 4.0 and greater)
    /// </summary>
    /// <remarks>
    /// It's a bit slow (as it uses locks), and only provides a small subset of the interface
    /// Overall, the implementation is intended to be simple & robust
    /// </remarks>
    public class ConcurrentQueue<T>
    {
        private readonly System.Object queueLock = new System.Object();
        private readonly Queue<T> queue = new Queue<T>();
        public void Enqueue(T item)
        {
            lock (queueLock)
            {
                queue.Enqueue(item);
            }
        }
        public bool TryDequeue(out T result)
        {
            lock (queueLock)
            {
                if (queue.Count == 0)
                {
                    result = default(T);
                    return false;
                }
                result = queue.Dequeue();
                return true;
            }
        }
    }
}
