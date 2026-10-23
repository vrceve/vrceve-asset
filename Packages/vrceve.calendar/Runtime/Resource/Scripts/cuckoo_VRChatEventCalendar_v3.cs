
using System;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Data;
using VRC.SDK3.Image;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon;

#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UnityEditor;
using UdonSharpEditor;
#endif

[AddComponentMenu("VRChatEventCalender/VRChatEventCalender v3")]
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class cuckoo_VRChatEventCalendar_v3 : UdonSharpBehaviour
{
    private const int MAX_EVENT_LENGTH = 256;
    private const int MAX_GENRE_LENGTH = 16;
    private const int MAX_DAYHEADER_LENGTH = 10;

    /// <summary>
    /// 近くにいる間のUpdateLoopの間隔(秒)
    /// </summary>
    private const float LOOP_INTERVAL = 0.25f;

    /// <summary>
    /// スクロール時に表示範囲を更新する最短の間隔(秒)
    /// </summary>
    private const float VISIBLE_UPDATE_INTERVAL = 0.1f;

    private const int MAX_ROWS = MAX_DAYHEADER_LENGTH + MAX_EVENT_LENGTH;

    private const string FORMAT_TIME_WITHOUT_SECOND = "HH:mm";

    /// <summary>
    /// Udon Debugに出力する時に見やすいように
    /// </summary>
    /// <param name="text"></param>
    private void DebugLog(string text) => Debug.Log($"[<color=yellow>VRChatEventCalender_v3</color>]{text}");
    private void DebugError(string text) => Debug.Log($"[<color=red>VRChatEventCalender_v3</color>]{text}");

    #region Variables
    /// <summary>
    /// 初期化中ならTrue
    /// </summary>
    private bool isReloading;

    /// <summary>
    /// ジャンルフィルターを全てOFFにする場合はTrue
    /// </summary>
    private bool isAllOFF;

    /// <summary>
    /// Jsonデータのダウンロードが失敗した際にTrueになるフラグ
    /// </summary>
    private bool hasJsonError;

    /// <summary>
    /// 上部に表示される画像データのダウンロードに失敗した際にTrueになるフラグ
    /// </summary>
    private bool hasHeaderLoadError;

    /// <summary>
    /// データのリロードまでのインターバル、2回目以降から300に固定される
    /// </summary>
    public float reloadInterval = 7f;

    /// <summary>
    /// 前回のリロードからの経過時間
    /// </summary>
    public float currentTime = 0f;

    /// <summary>
    /// ONの場合、最初の1回の読み込みのあとは、プレイヤーがこの範囲内にいる間だけ更新処理を行う(負荷軽減、特にQuest向け)
    /// </summary>
    public bool useActiveRange = true;

    /// <summary>
    /// 更新処理を行う範囲(メートル)。このオブジェクトの位置からの距離
    /// </summary>
    public float activeRange = 5f;

    /// <summary>
    /// リロード中のステップ？ 1f遅らせてreloadCalを動かすためにあるかも
    /// </summary>
    private int reloadStep = 0;

    /// <summary>
    /// 読み込み時に表示される画像
    /// </summary>
    [SerializeField] GameObject NOWLOADING;

    /// <summary>
    /// 画面上部に出るイラストのオブジェクト
    /// </summary>
    [SerializeField] GameObject HeaderImage;

    /// <summary>
    /// イベントの詳細を表示するボックスのゲームオブジェクト
    /// </summary>
    [SerializeField] GameObject ContentImage;

    /// <summary>
    /// クエスト対応のみを表示するかのToggle
    /// </summary>
    [SerializeField] Toggle questToggle;

    /// <summary>
    /// 上部に表示されるヘッダーのマテリアル、テクスチャを差し替えるのに使う
    /// </summary>
    [SerializeField] Material mat_header;

    /// <summary>
    /// 上部に表示されるヘッダーのイラストをダウンロードする際の設定
    /// 元々定義してあって、基本的にはインスペクターでは非表示
    /// </summary>
    [SerializeField] TextureInfo info;

    /// <summary>
    /// 上部に表示されるヘッダーのイラストのダウンロードリンク
    /// </summary>
    [SerializeField] VRCUrl url_header;

    /// <summary>
    /// イベントのデータが入ってるJsonを取得するリンク
    /// </summary>
    [SerializeField] VRCUrl url_json;

    /// <summary>
    /// 詳細(body, condition, way, note)を取得するURL。空の場合は一覧に含まれる詳細をそのまま使う(旧形式)
    /// </summary>
    [SerializeField] VRCUrl url_details;

    /// <summary>
    /// イベントのリストを表示するところの親オブジェクト
    /// </summary>
    [SerializeField] Transform Content;

    /// <summary>
    /// 受信したデータを受け取らせるUdonBehaviour
    /// </summary>
    [SerializeField] UdonBehaviour Receiver;

    /// <summary>
    /// イベントのリストを表示するScrollViewのゲームオブジェクト
    /// </summary>
    [SerializeField] ScrollRect _scrollRect;

    /// <summary>
    /// ジャンルフィルターの親オブジェクト
    /// </summary>
    [SerializeField] RectTransform Filter;

    /// <summary>
    /// イベントの詳細を表示するパネル
    /// </summary>
    [SerializeField] RectTransform contentPanelRect;

    /// <summary>
    /// イベントの説明をいれるテキスト
    /// </summary>
    [SerializeField] Text _Day, _Time, _Title, _Quest, _Author, _Body, _Genre, _Conditions, _Way, _Note;

    /// <summary>
    /// Jsonから与えられる時間のフォーマット 例: 2023-06-29T22:00:00.000
    /// </summary>
    string format = "yyyy-MM-ddTHH:mm:ss.fff";

    /// <summary>
    /// クエスト対応であるかを格納してる配列
    /// </summary>
    bool[] quests;

    /// <summary>
    /// イベントの名前を格納してる配列
    /// </summary>
    string[] titles;

    /// <summary>
    /// イベント開始時間を格納してる配列
    /// </summary>
    DateTime[] start_times;

    /// <summary>
    /// イベント終了時間を格納してる配列
    /// </summary>
    DateTime[] end_times;

    /// <summary>
    /// イベント主催者を格納してる配列
    /// </summary>
    string[] authors;

    /// <summary>
    /// 説明文を格納してる配列
    /// </summary>
    string[] bodys;

    /// <summary>
    /// 参加条件を格納してる配列
    /// </summary>
    string[] conditions;

    /// <summary>
    /// 参加方法を格納してる配列
    /// </summary>
    string[] ways;

    /// <summary>
    /// イベントのノートを格納してる配列
    /// </summary>
    string[] notes;

    /// <summary>
    /// イベント毎のIDを格納してる配列
    /// これを元にIndexを取得する
    /// </summary>
    string[] ids;

    /// <summary>
    /// イベントに付与されたジャンルを格納してる配列
    /// </summary>
    DataList[] genres;

    /// <summary>
    /// Jsonから取得したジャンルのリストを格納してる配列
    /// </summary>
    DataList genres_filterlist;

    /// <summary>
    /// Jsonから取得したジャンル毎のイベント数を格納してる配列
    /// </summary>
    DataList genres_eventcount;

    /// <summary>
    /// Toggleがオンである (表示していいイベント)のジャンル名を格納している配列
    /// </summary>
    DataList genreFilters;

    /// <summary>
    /// 上部に表示されるヘッダーのイラストをダウンロードするクラス
    /// </summary>
    private VRCImageDownloader imgDownloader;

    private Texture2D cacheTexture;

    /// <summary>
    /// ダウンロードした際の結果
    /// </summary>
    private IVRCImageDownload _task;

    /// <summary>
    /// イベントのリストを表示する最初のポジション
    /// ここからどんどん足してく
    /// </summary>
    Vector2 DefaultPositionDayRect = new Vector2(491f, -25.0f);

    private RectTransform[] dayHeaders = new RectTransform[MAX_DAYHEADER_LENGTH];
    private RectTransform[] eventButtons = new RectTransform[MAX_EVENT_LENGTH];
    private eventcalendar_showcontent[] eventButtonScripts = new eventcalendar_showcontent[MAX_EVENT_LENGTH];

    private Text[] dayHeaderTexts = new Text[MAX_DAYHEADER_LENGTH];
    private Text[] eventButtonTexts = new Text[MAX_EVENT_LENGTH];

    private Text[] eventButtonTimeTexts = new Text[MAX_EVENT_LENGTH];

    private int checkboxLen = 0;
    private eventcalendar_checkbox[] checkboxes = new eventcalendar_checkbox[MAX_GENRE_LENGTH];
    private Text[] checkboxTexts = new Text[MAX_GENRE_LENGTH];

    private Vector3[] worldCorners = new Vector3[4];

    //---- 表示範囲の計算用(行は 日付見出し→その日のイベント… の順で、上から下へ並ぶ)
    /// <summary>
    /// 日付見出し・イベントボタンそれぞれの、自身の位置(localPosition)から見た上下の端
    /// </summary>
    private float[] dayRectLo = new float[MAX_DAYHEADER_LENGTH];
    private float[] dayRectHi = new float[MAX_DAYHEADER_LENGTH];
    private float[] eventRectLo = new float[MAX_EVENT_LENGTH];
    private float[] eventRectHi = new float[MAX_EVENT_LENGTH];

    private int rowCount = 0;
    private bool[] rowIsEvent = new bool[MAX_ROWS];
    private int[] rowSlot = new int[MAX_ROWS];
    private float[] rowPosY = new float[MAX_ROWS];
    private float[] rowLo = new float[MAX_ROWS];
    private float[] rowHi = new float[MAX_ROWS];

    /// <summary>
    /// 現在表示している行の範囲(visFirst〜visLast)。空のときはvisLast &lt; visFirst
    /// </summary>
    private int visFirst = 0;
    private int visLast = -1;

    /// <summary>
    /// viewportの上端(contentの親の座標系)とviewportの高さ(contentの座標系)。レイアウト時に1回だけ計算する
    /// </summary>
    private float viewTopInParent = 0f;
    private float viewHeightLocal = 0f;
    private float contentScaleY = 1f;

    private float lastVisibleUpdate = -1000f;
    private bool visibleUpdatePending = false;

    /// <summary>
    /// ボタン(slot)に割り当てるイベントindexと、実際にテキストを設定済みのイベントindex
    /// </summary>
    private int[] slotEventIndex = new int[MAX_EVENT_LENGTH];
    private int[] slotFilledIndex = new int[MAX_EVENT_LENGTH];
    private int usedDayCount = 0;
    private int usedEventCount = 0;

    /// <summary>
    /// 各イベントの開始日文字列(yyyy-MM-dd (曜))。受信時に1回だけ作る
    /// </summary>
    string[] dayStrs;

    /// <summary>
    /// 各イベントの時刻表示文字列(HH:mm ~ HH:mm)。受信時に1回だけ作る
    /// </summary>
    string[] timeStrs;

    /// <summary>
    /// 直前のイベントの日付部分(yyyy-MM-dd)とその表示文字列。同じ日付なら再利用する
    /// </summary>
    private string lastDatePart = "";
    private string lastDayStr = "";

    private string[] weekdayNames = new string[] { "日", "月", "火", "水", "木", "金", "土" };

    /// <summary>
    /// ヘッダー画像を次に取得してよい時刻(UTCの実時間のTicks。Time.timeはウィンドウ非フォーカスやQuestの装着解除で止まるため使わない)
    /// </summary>
    private long nextHeaderTicks = 0;
    private bool headerRequested = false;

    private float lastReloadStart = -1000f;

    /// <summary>
    /// 前回正常に展開できたJson。同じ内容なら再構築しない
    /// </summary>
    private string lastJson = null;

    /// <summary>
    /// 一度でも正常にデータを取得できたらTrue
    /// </summary>
    private bool hasData = false;

    private int failCount = 0;

    /// <summary>
    /// /events はサーバー側で毎正時(:00)に切り替わるので、次の正時の少し後に取得する(初回・失敗後・Interact時は即時)
    /// </summary>
    private const float EVENTS_SLOT_PERIOD = 3600f;
    private bool eventsDue = true;
    private long nextEventsTicks = 0;

    /// <summary>
    /// /image はサーバー側で5分境界(:00, :05, :10...)に切り替わるので、境界の少し後に取得する
    /// </summary>
    private const float HEADER_SLOT_PERIOD = 300f;

    /// <summary>
    /// サーバーはstale-while-revalidateが60秒あるため、枠の切り替わりから最低これだけ待って取得する
    /// </summary>
    private const float SLOT_MIN_DELAY = 65f;

    /// <summary>
    /// 全員が同時にアクセスしないよう、SLOT_MIN_DELAYに加えるランダムなばらつきの最大秒数
    /// </summary>
    private const float SLOT_JITTER = 25f;

    /// <summary>
    /// 範囲から出たときにすぐ切り替わらないよう、範囲内→範囲外の判定にだけ足す余裕(メートル)
    /// </summary>
    private const float ACTIVE_RANGE_MARGIN = 3f;

    /// <summary>
    /// 範囲外のときの、範囲内に入ったかの確認間隔(秒)
    /// </summary>
    private const float FAR_CHECK_INTERVAL = 1f;

    private bool isNear = true;
    private float lastLoopTime = 0f;

    /// <summary>
    /// VRCStringDownloaderの5秒制限を守るための、取得間隔と取得中フラグ
    /// </summary>
    private const float STRING_LOAD_GAP = 5.5f;
    private float lastStringLoadTime = -1000f;
    private bool listLoading = false;
    private bool detailsLoading = false;

    /// <summary>
    /// 一覧に詳細が含まれていた(旧形式)場合はTrue。この場合は詳細を取得しない
    /// </summary>
    private bool inlineDetails = false;

    private string listVersion = "";
    private string detailsVersion = "";
    private DataDictionary detailsDict = null;

    /// <summary>
    /// 詳細の取得待ちのイベントID。取得が終わったら詳細ボックスを描き直す
    /// </summary>
    private string pendingDetailId = null;
    private bool pendingDetailsRequest = false;
    private float nextDetailsAllowed = 0f;
    private int detailFailCount = 0;

    /// <summary>
    /// 一覧のversionが変わったあと、古い詳細を取り直す要求を出したversion
    /// </summary>
    private string detailsRefreshedFor = "";

    /// <summary>
    /// versionの食い違いで一覧を取り直す要求を出した詳細側のversion
    /// </summary>
    private string listRefetchFor = "";

    /// <summary>
    /// 詳細ボックスに今表示しているイベントのindex(表示していなければ-1)
    /// </summary>
    private int drawnIndex = -1;
    private const float INTERACT_RELOAD_COOLDOWN = 30f;
    #endregion

    #region Internal Func
    private void Start()
    {
        //インスタンス作成
        imgDownloader = new VRCImageDownloader();

        //詳細を表示するボックスは一応非表示に
        ContentImage.SetActive(false);
        _scrollRect.enabled = true;

        //Loading画面を表示
        NOWLOADING.SetActive(true);

        //最初にリセットしておく
        ResetButton();

        GetEventFields();

        //フィルタをリセット
        InitializeFields();

        //Androidモードかチェック
        CheckIsAndroid();

        lastLoopTime = Time.time;

        //ループ開始
        UpdateLoop();
    }

    private void OnDestroy()
    {
        //破棄
        imgDownloader.Dispose();
    }

    /// <summary>
    /// ループ、300秒に1回更新が入る
    /// </summary>
    public void UpdateLoop()
    {
        //範囲外のときはループ間隔が空くため、実際の経過時間を使う
        float deltaTime = Time.time - lastLoopTime;
        lastLoopTime = Time.time;

        //最初の1回の読み込みが終わったあと、範囲外にいる間は何もしない(読み込み中のものは最後まで進める)
        if (useActiveRange && hasData && reloadStep == 0)
        {
            UpdateIsNear();
            if (!isNear)
            {
                SendCustomEventDelayedSeconds(nameof(UpdateLoop), FAR_CHECK_INTERVAL, VRC.Udon.Common.Enums.EventTiming.Update);
                return;
            }
        }

        //詳細の取得待ちがあれば、5秒制限を守れるタイミングで取得する
        TryDispatchDetails();

        //読み込み (初回のみ7秒)
        currentTime += deltaTime;

        //通常の再読み込み(初回・失敗後の再試行)に加えて、ヘッダー画像とイベント一覧は
        //サーバー側の切り替わり(5分境界・正時)の直後に取得する
        if (reloadStep == 1 || currentTime > reloadInterval
            || (headerRequested && NowTicks() >= nextHeaderTicks)
            || (hasData && NowTicks() >= nextEventsTicks))
        {
            reloadCal();
        }

        SendCustomEventDelayedSeconds(nameof(UpdateLoop), LOOP_INTERVAL, VRC.Udon.Common.Enums.EventTiming.Update);
    }
    #endregion

    /// <summary>
    /// クリック時に更新
    /// </summary>
    public override void Interact()
    {
        if (Time.time - lastReloadStart < INTERACT_RELOAD_COOLDOWN)
            return;

        eventsDue = true;
        nextHeaderTicks = 0;
        currentTime = reloadInterval + 1;
        reloadStep = 0;
    }

    /// <summary>
    /// ローカルプレイヤーがこのオブジェクトの範囲内にいるかを更新する
    /// </summary>
    private void UpdateIsNear()
    {
        VRCPlayerApi player = Networking.LocalPlayer;
        if (!Utilities.IsValid(player))
        {
            isNear = true;
            return;
        }

        float distance = Vector3.Distance(player.GetPosition(), transform.position);
        float limit = isNear ? activeRange + ACTIVE_RANGE_MARGIN : activeRange;
        isNear = distance <= limit;
    }

    #region Optimize Func

    #endregion

    #region DateTime Func
    /// <summary>
    /// 与えられたjsonからDateTimeに変換
    /// </summary>
    /// <param name="timeStr"></param>
    /// <param name="time"></param>
    private bool GetTimeFromStr(string timeStr, out DateTime time)
    {
        int _Plus = timeStr.IndexOf('+');
        if (_Plus == -1)
        {
            DebugLog("Plus is not found");
            time = new DateTime();
            return false;
        }

        //日付を変換
        string _time = timeStr.Substring(0, _Plus);
        return TryParseTimeStr(_time, out time);
    }

    /// <summary>
    /// s の start から len 桁の数字を整数にする(数字であることは確認済みの前提)
    /// </summary>
    private int Num(string s, int start, int len)
    {
        int n = 0;
        for (int i = 0; i < len; i++)
            n = n * 10 + ((int)s[start + i] - 48);
        return n;
    }

    /// <summary>
    /// yyyy-MM-ddTHH:mm:ss.fff の形式と値の範囲を確認し、問題なければDateTimeを直接作る(ParseExactは使わない)
    /// </summary>
    private bool TryParseTimeStr(string s, out DateTime time)
    {
        time = new DateTime();

        if (s.Length != 23)
            return false;

        for (int i = 0; i < 23; i++)
        {
            char c = s[i];
            if (i == 4 || i == 7)
            {
                if (c != '-') return false;
            }
            else if (i == 10)
            {
                if (c != 'T') return false;
            }
            else if (i == 13 || i == 16)
            {
                if (c != ':') return false;
            }
            else if (i == 19)
            {
                if (c != '.') return false;
            }
            else if (c < '0' || c > '9')
            {
                return false;
            }
        }

        int year = Num(s, 0, 4);
        int month = Num(s, 5, 2);
        int day = Num(s, 8, 2);
        int hour = Num(s, 11, 2);
        int minute = Num(s, 14, 2);
        int second = Num(s, 17, 2);
        int millisecond = Num(s, 20, 3);

        if (year < 1 || month < 1 || month > 12 || day < 1)
            return false;
        if (hour > 23 || minute > 59 || second > 59)
            return false;

        int maxDay = 31;
        if (month == 4 || month == 6 || month == 9 || month == 11)
            maxDay = 30;
        else if (month == 2)
            maxDay = (year % 4 == 0 && (year % 100 != 0 || year % 400 == 0)) ? 29 : 28;

        if (day > maxDay)
            return false;

        time = new DateTime(year, month, day, hour, minute, second, millisecond);
        return true;
    }

    /// <summary>
    /// イベントのデータを初期化
    /// </summary>
    /// <param name="count"></param>
    private void InitializeEventDatas(int count)
    {
        quests = new bool[count];
        titles = new string[count];
        start_times = new DateTime[count];
        end_times = new DateTime[count];
        authors = new string[count];
        bodys = new string[count];
        conditions = new string[count];
        ways = new string[count];
        notes = new string[count];
        ids = new string[count];
        dayStrs = new string[count];
        timeStrs = new string[count];
        genres = new DataList[count];

        //イベントの内容が入れ替わるので、ボタンに設定済みのテキストは無効にする
        for (int i = 0; i < MAX_EVENT_LENGTH; i++)
            slotFilledIndex[i] = -1;
    }
    #endregion

    /// <summary>
    /// 更新時に呼ばれる関数、更新時だけNOWLOADINGを表示
    /// </summary>
    public void reloadCal()
    {
        //エラーが起きた場合は再リロードさせる
        if (reloadStep == 0)
        {
            //一覧を取得するときに、詳細の取得中や直前の取得から5秒未満なら待つ
            if ((eventsDue || NowTicks() >= nextEventsTicks) && !CanLoadString())
                return;

            //初回(データ未取得)のときだけローディングを表示し、取得済みなら今の表示を保ったまま裏で更新する
            if (!hasData)
                NOWLOADING.SetActive(true);

            lastReloadStart = Time.time;

            // ヘッダー画像は5分刻みで変わるが、Interact連打などの短い間隔での再取得は避ける
            if (!headerRequested || hasHeaderLoadError || NowTicks() >= nextHeaderTicks)
            {
                headerRequested = true;
                nextHeaderTicks = TicksAfter(SecondsUntilNextSlot(HEADER_SLOT_PERIOD));

                // ダウンロード実行
                _task = imgDownloader.DownloadImage(
                    url_header,
                    mat_header,
                    Receiver,
                    info);
            }

            //イベント一覧は毎正時の直後に1回(初回・失敗後・Interact時は即時)だけ取得する
            if (eventsDue || NowTicks() >= nextEventsTicks)
            {
                eventsDue = false;
                nextEventsTicks = TicksAfter(SecondsUntilNextSlot(EVENTS_SLOT_PERIOD));
                listLoading = true;
                lastStringLoadTime = Time.time;
                VRCStringDownloader.LoadUrl(url_json, Receiver);
            }

            //処理をdeltaTime分ずらすため？
            reloadStep = 1;
        }
        else if (reloadStep == 1)
        {
            //Cal.SetActive(true);

            //タイマーをリセット
            reloadStep = 0;
            currentTime = 0;
            reloadInterval = 60 * 5; //初回以降は５分に１回更新
        }
    }

    private long NowTicks()
    {
        return DateTime.UtcNow.Ticks;
    }

    /// <summary>
    /// 今からseconds秒後の時刻(UTCのTicks)
    /// </summary>
    private long TicksAfter(float seconds)
    {
        return DateTime.UtcNow.Ticks + (long)((double)seconds * 10000000.0);
    }

    /// <summary>
    /// 次に取得する枠(period秒刻み、UTC基準の分と秒)の切り替わりから SLOT_MIN_DELAY + ばらつき だけ後までの秒数を返す。
    /// 今の枠がすでにSLOT_MIN_DELAYを過ぎていれば最新の内容を取得済みとみなし、次の枠を待つ
    /// </summary>
    private float SecondsUntilNextSlot(float period)
    {
        DateTime now = DateTime.UtcNow;
        float s = now.Minute * 60f + now.Second + now.Millisecond * 0.001f;
        float slotStart = Mathf.Floor(s / period) * period;

        float target = slotStart + SLOT_MIN_DELAY + UnityEngine.Random.Range(0f, SLOT_JITTER);
        if (s >= slotStart + SLOT_MIN_DELAY)
            target += period;

        return target - s;
    }

    #region Funcs
    /// <summary>
    /// 生成されたIDからイベント番号を取得
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    private int getIndexbyID(string id)
    {
        for (int i = 0; i < ids.Length; i++)
        {
            if (ids[i] == id)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// ジャンルフィルターを元に、イベントが通していいジャンルか判定
    /// </summary>
    /// <param name="genres">イベント側のジャンルリスト</param>
    /// <param name="genrefilters">ジャンルフィルター</param>
    /// <returns></returns>
    private bool checkGenre(DataList genres, DataList genrefilters)
    {
        for (int j = 0; j < genrefilters.Count; j++)
        {
            if (genrefilters[j].TokenType != TokenType.String)
                continue;

            string genre = (string)genrefilters[j];

            //ジャンルが被ってる場合は通す
            if (genres.IndexOf(genre) != -1)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 元あったフィルタの情報を元に、更新後のフィルタにチェックを入れるか判定する
    /// </summary>
    /// <param name="filterName">フィルタ名</param>
    /// <param name="genreFilter">前のジャンルフィルターでONだった物</param>
    /// <param name="allFilter">前回取得したジャンルリスト</param>
    /// <returns></returns>
    private bool checkBeforeFilter(string filterName, DataList genreFilter, DataList allFilter)
    {
        if (genreFilter == null || allFilter == null)
            return true;

        if (allFilter.Count <= 0)
            return true;

        if (allFilter.IndexOf(filterName) == -1)
            return true;

        return genreFilter.IndexOf(filterName) != -1;
    }

    /// <summary>
    /// イベントデータを日付によって分ける。[日付文字列, イベントindexのリスト] のリストを返す
    /// </summary>
    private DataList splitArrayByDate(bool isQuest = false, DataList genreFilters = null)
    {
        if (genreFilters.Count == 0)
            return null;

        DataList dayStrings = new DataList();
        DataList dayIndexLists = new DataList();
        for (int i = 0; i < dayStrs.Length; i++)
        {
            string dayStr = dayStrs[i];
            if (string.IsNullOrEmpty(dayStr))
            {
                DebugLog($"dayStr is empty. {i}");
                continue;
            }

            if (!checkGenre(genres[i], genreFilters))
                continue;

            if (isQuest && !quests[i])
                continue;

            int d = dayStrings.IndexOf(dayStr);
            if (d == -1)
            {
                dayStrings.Add(dayStr);
                DataList indexList = new DataList();
                indexList.Add(i);
                dayIndexLists.Add(indexList);
            }
            else
            {
                dayIndexLists[d].DataList.Add(i);
            }
        }

        if (dayStrings.Count < 1)
            return null;

        DataList splitedArray = new DataList();
        for (int i = 0; i < dayStrings.Count; i++)
        {
            DataList splited = new DataList();
            splited.Add(dayStrings[i]);
            splited.Add(dayIndexLists[i]);
            splitedArray.Add(splited);
        }

        return splitedArray;
    }

    /// <summary>
    /// 全てのボタンを非表示にして見えない位置へ移動する(起動時に1回だけ)
    /// </summary>
    private void ResetButton()
    {
        //ポジションをリセット
        RectTransform RectTransformContent = Content.GetComponent<RectTransform>();

        if (RectTransformContent != null)
            RectTransformContent.localPosition = new Vector3(5, 0, 0);

        //規定のポジションから開始
        Vector2 dayHeaderPosition = DefaultPositionDayRect;

        //見えない位置へ移動
        dayHeaderPosition.x = dayHeaderPosition.x + 1500;

        rowCount = 0;
        visFirst = 0;
        visLast = -1;
        usedDayCount = 0;
        usedEventCount = 0;

        for (int i = 0; i < MAX_DAYHEADER_LENGTH; i++)
        {
            if (dayHeaders[i] == null)
                continue;

            dayHeaders[i].localPosition = dayHeaderPosition;
            dayHeaders[i].gameObject.SetActive(false);
        }

        for (int i = 0; i < MAX_EVENT_LENGTH; i++)
        {
            if (eventButtons[i] == null)
                continue;

            eventButtons[i].localPosition = dayHeaderPosition;
            eventButtons[i].gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 再構築の前に、表示中の行だけを非表示にして行の情報を空にする(表示していない行は元から非表示)
    /// </summary>
    private void ResetRows()
    {
        RectTransform RectTransformContent = Content.GetComponent<RectTransform>();

        if (RectTransformContent != null)
            RectTransformContent.localPosition = new Vector3(5, 0, 0);

        for (int r = visFirst; r <= visLast; r++)
            SetRowActive(r, false);

        rowCount = 0;
        visFirst = 0;
        visLast = -1;
        usedDayCount = 0;
        usedEventCount = 0;
    }

    /// <summary>
    /// 行の表示・非表示を切り替える。表示するときに位置とテキストを設定する
    /// </summary>
    private void SetRowActive(int r, bool active)
    {
        int slot = rowSlot[r];
        RectTransform rt = rowIsEvent[r] ? eventButtons[slot] : dayHeaders[slot];
        if (rt == null)
            return;

        if (active)
        {
            rt.localPosition = new Vector3(DefaultPositionDayRect.x, rowPosY[r], 0f);
            if (rowIsEvent[r])
                FillEventButton(slot);
        }

        GameObject go = rt.gameObject;
        if (go.activeSelf != active)
            go.SetActive(active);
    }

    /// <summary>
    /// ボタンに割り当てたイベントの内容を設定する。すでに同じイベントを設定済みなら何もしない
    /// </summary>
    private void FillEventButton(int slot)
    {
        int index = slotEventIndex[slot];
        if (slotFilledIndex[slot] == index)
            return;

        slotFilledIndex[slot] = index;
        eventButtonScripts[slot].setEventID(ids[index]);

        Text buttonText = eventButtonTexts[slot];
        if (buttonText != null)
            buttonText.text = titles[index];

        Text timeText = eventButtonTimeTexts[slot];
        if (timeText != null)
            timeText.text = timeStrs[index] != null ? timeStrs[index] : "";
    }

    /// <summary>
    /// viewportの上端と高さをcontentの座標系で求めてキャッシュする(レイアウトのたびに1回だけ)
    /// </summary>
    private void CacheViewport()
    {
        RectTransform viewport = _scrollRect.viewport;
        viewport.GetWorldCorners(worldCorners);

        float y0 = Content.InverseTransformPoint(worldCorners[0]).y;
        float y1 = Content.InverseTransformPoint(worldCorners[1]).y;
        float top = y0 > y1 ? y0 : y1;
        float bottom = y0 > y1 ? y1 : y0;

        contentScaleY = Content.localScale.y;
        if (contentScaleY == 0f)
            contentScaleY = 1f;

        viewHeightLocal = top - bottom;
        viewTopInParent = Content.localPosition.y + contentScaleY * top;
    }

    /// <summary>
    /// 見えている行だけを表示する。前回から変わった行だけを切り替える
    /// </summary>
    private void UpdateVisibleRows()
    {
        lastVisibleUpdate = Time.time;

        if (rowCount == 0)
            return;

        float top = (viewTopInParent - Content.localPosition.y) / contentScaleY;
        float bottom = top - viewHeightLocal;

        //rowLo は上の行ほど大きいので、top以下になる最初の行が見え始める行
        int lo = 0;
        int hi = rowCount;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (rowLo[mid] <= top)
                hi = mid;
            else
                lo = mid + 1;
        }
        int first = lo;

        //rowHi は下の行ほど小さいので、bottom未満になる最初の行の1つ前が最後に見える行
        lo = 0;
        hi = rowCount;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (rowHi[mid] < bottom)
                hi = mid;
            else
                lo = mid + 1;
        }
        int last = lo - 1;

        int oldFirst = visFirst;
        int oldLast = visLast;

        for (int r = oldFirst; r <= oldLast; r++)
        {
            if (r < first || r > last)
                SetRowActive(r, false);
        }

        for (int r = first; r <= last; r++)
        {
            if (r < oldFirst || r > oldLast)
                SetRowActive(r, true);
        }

        visFirst = first;
        visLast = last;
    }

    private void AddRow(bool isEvent, int slot, float posY, float lo, float hi)
    {
        rowIsEvent[rowCount] = isEvent;
        rowSlot[rowCount] = slot;
        rowPosY[rowCount] = posY;
        rowLo[rowCount] = posY + lo;
        rowHi[rowCount] = posY + hi;
        rowCount++;
    }

    /// <summary>
    /// 分けられたデータを元にイベントのリストを作成 ボタンにIDを入れる
    /// </summary>
    private void CreateButtonFromDataList(DataList splitedDataList)
    {
        //前回の表示をリセットする
        ResetRows();

        if (splitedDataList == null)
            return;

        float __rectHeight = DefaultPositionDayRect.y;
        float __rectSizeY = 0.0f;
        int __rectNum = 0;

        int __loopCount = splitedDataList.Count;
        if (__loopCount > MAX_DAYHEADER_LENGTH)
            __loopCount = MAX_DAYHEADER_LENGTH;

        for (int i = 0; i < __loopCount; i++)
        {
            __rectSizeY -= 45;
            __rectSizeY -= 30 * ((DataList)splitedDataList[i].DataList[1]).Count;
            __rectSizeY -= MAX_DAYHEADER_LENGTH;
        }

        Vector2 sizeDelta = _scrollRect.content.sizeDelta;
        sizeDelta.y = __rectSizeY * -1;

        _scrollRect.content.sizeDelta = sizeDelta;

        for (int i = 0; i < __loopCount; i++)
        {
            DataList dayToken = splitedDataList[i].DataList;
            string DayStr = (string)dayToken[0];
            DataList indexList = (DataList)dayToken[1];

            AddRow(false, i, __rectHeight, dayRectLo[i], dayRectHi[i]);

            if (dayHeaderTexts[i] != null)
                dayHeaderTexts[i].text = DayStr;

            __rectHeight -= 45;

            for (int j = 0; j < indexList.Count; j++)
            {
                int index = indexList[j].Int;

                //最大数を上回った場合はスキップ
                if (__rectNum >= MAX_EVENT_LENGTH)
                {
                    Debug.LogWarning($"Event count limit exceeded (MAX_EVENT_LENGTH).");
                    break;
                }

                AddRow(true, __rectNum, __rectHeight, eventRectLo[__rectNum], eventRectHi[__rectNum]);
                slotEventIndex[__rectNum] = index;

                __rectHeight -= 30;
                __rectNum++;
            }

            __rectHeight -= 10;
        }
        usedDayCount = __loopCount;
        usedEventCount = __rectNum;

        //viewportの位置と高さを求めて、見える行を表示する(位置とテキストは表示するときに設定する)
        CacheViewport();
        UpdateVisibleRows();
    }

    /// <summary>
    /// 受け取ったデータを元にイベントのリストを再構築
    /// </summary>
    /// <param name="isQuest">クエスト対応のみ</param>
    /// <param name="genreFilters">ジャンルフィルター</param>
    private void ReloadEvents(bool isQuest, DataList genreFilters)
    {
        //恐らくないけど、誤ってリロードイベントが呼ばれた場合にスキップ
        if (currentTime > reloadInterval)
        {
            DebugLog("Skipped reload event by already reloading");
            return;
        }

        //日付でデータを分ける
        DataList splitedArr = splitArrayByDate(isQuest, genreFilters);

        //取得したイベントデータからボタンを作成
        CreateButtonFromDataList(splitedArr);
    }

    /// <summary>
    /// 受け取ったjsonデータを元に各配列へデータを分ける
    /// </summary>
    /// <param name="events">受け取ったデータ</param>
    private void DeserializeEvents(DataList events)
    {
        inlineDetails = false;
        lastDatePart = "";
        lastDayStr = "";
        InitializeEventDatas(events.Count);
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i].TokenType == TokenType.String)
            {
                DebugLog($"str = {events[i].String}");
            }
            else if (events[i].TokenType == TokenType.DataDictionary)
            {
                DataDictionary dic = events[i].DataDictionary;
                DataToken t;
                string startHM = null;

                //旧形式(/events: 一覧に詳細を含み、HTMLの実体参照が残っている)のときだけ文字参照を戻す
                bool legacy = dic.ContainsKey("body");
                if (legacy) inlineDetails = true;

                if (dic.TryGetValue("id", out t) && t.TokenType == TokenType.String) ids[i] = t.String;
                if (dic.TryGetValue("title", out t) && t.TokenType == TokenType.String) titles[i] = legacy ? ReplaceCharacterReference(t.String) : NeutralizeTags(t.String);
                if (dic.TryGetValue("author", out t) && t.TokenType == TokenType.String) authors[i] = legacy ? t.String : NeutralizeTags(t.String);
                if (dic.TryGetValue("body", out t) && t.TokenType == TokenType.String) bodys[i] = ReplaceCharacterReference(t.String);
                if (dic.TryGetValue("condition", out t) && t.TokenType == TokenType.String) conditions[i] = t.String;
                if (dic.TryGetValue("way", out t) && t.TokenType == TokenType.String) ways[i] = ReplaceCharacterReference(t.String);
                if (dic.TryGetValue("note", out t) && t.TokenType == TokenType.String) notes[i] = ReplaceCharacterReference(t.String);
                if (dic.TryGetValue("quest", out t) && t.TokenType == TokenType.Boolean) quests[i] = t.Boolean;
                if (dic.TryGetValue("genres", out t) && t.TokenType == TokenType.DataList) genres[i] = t.DataList;

                if (dic.TryGetValue("start", out t) && t.TokenType == TokenType.String)
                {
                    if (GetTimeFromStr(t.String, out DateTime _start))
                    {
                        start_times[i] = _start;

                        //同じ日付が続く間は、前回作った日付の表示文字列を再利用する
                        string datePart = t.String.Substring(0, 10);
                        if (datePart != lastDatePart)
                        {
                            lastDatePart = datePart;
                            lastDayStr = datePart + " (" + weekdayNames[(int)_start.DayOfWeek] + ")";
                        }

                        dayStrs[i] = lastDayStr;
                        startHM = t.String.Substring(11, 5);
                    }
                    else
                    {
                        DebugLog($"<color=red>Get Time Error start[{i}]</color>");
                    }
                }

                if (dic.TryGetValue("end", out t) && t.TokenType == TokenType.String)
                {
                    if (GetTimeFromStr(t.String, out DateTime _end))
                    {
                        end_times[i] = _end;
                        if (startHM != null)
                            timeStrs[i] = startHM + " ~ " + t.String.Substring(11, 5);
                    }
                    else
                        DebugLog($"<color=red>Get Time Error end[{i}]</color>");
                }

                if (genres[i] == null)
                    genres[i] = new DataList();
            }
        }

        //取得したイベントデータからボタンを作成
        ReloadEvents(questToggle.isOn, genreFilters);
    }

    /// <summary>
    /// Toggleが変更された場合に呼ばれて、全てのToggleの情報を元にジャンルフィルタを再構築
    /// </summary>
    private void RefleshGenres()
    {
        //一回全部オフにする
        //16は最大数
        for (int i = 0; i < MAX_GENRE_LENGTH; i++)
        {
            if (checkboxes[i] == null)
                continue;

            checkboxes[i].gameObject.SetActive(false);
        }

        checkboxLen = genres_filterlist.Count;
        if (checkboxLen > MAX_GENRE_LENGTH)
            checkboxLen = MAX_GENRE_LENGTH;
        for (int i = 0; i < checkboxLen; i++)
        {
            string genreName = (string)genres_filterlist[i];
            string genreCount = ((double)genres_eventcount[i]).ToString();

            if (Filter == null)
            {
                DebugLog("Filter is not found");
                continue;
            }

            if (checkboxes[i].GetOn() && !string.IsNullOrEmpty(checkboxes[i].filterName))
            {
                if (genreFilters.IndexOf(genreName) == -1)
                    genreFilters.Add(genreName);
            }

            checkboxes[i].gameObject.SetActive(true);
            checkboxTexts[i].text = $"{genreName} ({genreCount})";
        }
    }

    /// <summary>
    /// jsonを受け取った際にジャンルフィルターのリストを更新
    /// </summary>
    /// <param name="_genres"></param>
    private void DeserializeGenres(DataDictionary _genres)
    {
        DataList genreFiltersBuf = genreFilters;
        DataList allFiltersBuf = genres_filterlist;

        //一回初期化
        genreFilters = new DataList();

        genres_filterlist = _genres.GetKeys();
        genres_eventcount = _genres.GetValues();
        //genres_filterToggle = new bool[genres_filterlist.Count];

        for (int i = 0; i < genres_filterlist.Count && i < MAX_GENRE_LENGTH; i++)
        {
            string genreName = (string)genres_filterlist[i];
            string genreCount = ((double)genres_eventcount[i]).ToString();

            if (checkboxes[i] == null)
            {
                continue;
            }

            checkboxes[i].setEventFilter(genreName);
            checkboxes[i].toggleisON(checkBeforeFilter(genreName, genreFiltersBuf, allFiltersBuf));

            if (checkboxes[i].GetOn())
                genreFilters.Add(genreName);

            checkboxes[i].gameObject.SetActive(true);
            checkboxTexts[i].text = $"{genreName} ({genreCount})";
        }
    }

    /// <summary>
    /// jsonを受け取った際に配列へデータを分ける
    /// </summary>
    /// <param name="json"></param>
    private void DeserializeJson(string json)
    {
        isReloading = true;

        if (VRCJson.TryDeserializeFromJson(json, out DataToken result))
        {
            //だいたいDictionaryで返ってくる
            if (result.TokenType == TokenType.DataDictionary)
            {
                var __keys = result.DataDictionary.GetKeys();
                var __vals = result.DataDictionary.GetValues();

                int __genreCount = 0, __eventCount = 0;

                //表示を作り直すので詳細ボックスは閉じる
                closeContentBox();

                for (int i = 0; i < __keys.Count; i++)
                {
                    if (__keys[i].TokenType == TokenType.String)
                    {
                        var val = __vals[i];
                        if (__keys[i].String == "version")
                        {
                            listVersion = val.TokenType == TokenType.String ? val.String : val.ToString();
                        }

                        if (__keys[i].String == "genres" && val.TokenType == TokenType.DataDictionary)
                        {
                            DataDictionary _genres = val.DataDictionary;
                            if (_genres == null)
                                continue;

                            __genreCount = _genres.Count;
                            DeserializeGenres(_genres);
                        }

                        if (__keys[i].String == "events" && val.TokenType == TokenType.DataList)
                        {
                            DataList _events = val.DataList;

                            __eventCount = _events.Count;
                            DeserializeEvents(_events);
                        }
                    }
                }

                lastJson = json;
                hasData = true;
                DebugLog($"Json load success! Genre[{__genreCount}] Event[{__eventCount}]");
            }
            else
            {
                DebugLog($"Isnt Support Type {result.TokenType}");
            }
        }
        else
        {
            DebugLog($"Failed to Deserialize json {json} - {result}");
        }

        //ローディング画面を非表示
        NOWLOADING.SetActive(false);
        isReloading = false;
    }

    #region Networking Func
    /// <summary>
    /// ヘッダーイメージを読み込んだ際に成功した場合
    /// </summary>
    /// <param name="result"></param>
    public override void OnImageLoadSuccess(IVRCImageDownload result)
    {
        if (Utilities.IsValid(cacheTexture))
            Destroy(cacheTexture);

        hasHeaderLoadError = false;

        //受け取ったテクスチャをマテリアルへ反映
        //(一度非表示にしないとマテリアルが反映されなかった経緯があるため、確認できるまで残す)
        HeaderImage.SetActive(false);
        mat_header.mainTexture = result.Result;
        cacheTexture = result.Result;
        HeaderImage.SetActive(true);
    }

    /// <summary>
    /// ヘッダーイメージを読み込んだ際に失敗した場合
    /// </summary>
    /// <param name="result"></param>
    public override void OnImageLoadError(IVRCImageDownload result)
    {
        hasHeaderLoadError = true;

        var error = result.Error;
        var errorMessage = result.ErrorMessage;

        DebugError(error.ToString());
        DebugError(errorMessage.ToString());
    }

    /// <summary>
    /// イベントデータのダウンロードに成功した場合
    /// </summary>
    /// <param name="result"></param>
    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        if (IsDetailsUrl(result))
        {
            OnDetailsLoaded(result.Result);
            return;
        }

        listLoading = false;

        if (string.IsNullOrEmpty(result.Result))
        {
            OnReloadFailed();
            return;
        }

        hasJsonError = false;
        failCount = 0;

        //前回と同じ内容なら作り直さない(スクロール位置・フィルタも保たれる)
        if (hasData && result.Result == lastJson)
        {
            NOWLOADING.SetActive(false);
            return;
        }

        DeserializeJson(result.Result);
    }

    /// <summary>
    /// イベントデータのダウンロードに失敗した場合
    /// </summary>
    /// <param name="result"></param>
    public override void OnStringLoadError(IVRCStringDownload result)
    {
        DebugError(result.Error);
        DebugError(result.ErrorCode.ToString());

        if (IsDetailsUrl(result))
        {
            OnDetailsFailed();
            return;
        }

        listLoading = false;
        OnReloadFailed();
    }

    #region Details
    private bool HasDetailsUrl()
    {
        return url_details != null && !string.IsNullOrEmpty(url_details.Get());
    }

    /// <summary>
    /// 一覧に詳細が含まれていない新形式で、詳細URLが設定されている場合はTrue
    /// </summary>
    private bool NeedsDetailFetch()
    {
        return !inlineDetails && HasDetailsUrl();
    }

    private bool IsDetailsUrl(IVRCStringDownload result)
    {
        return HasDetailsUrl() && result.Url != null && result.Url.Get() == url_details.Get();
    }

    /// <summary>
    /// 一覧・詳細のどちらも取得中ではなく、直前の取得から5秒以上あいていればTrue
    /// </summary>
    private bool CanLoadString()
    {
        return !listLoading && !detailsLoading && Time.time - lastStringLoadTime >= STRING_LOAD_GAP;
    }

    private void TryDispatchDetails()
    {
        if (!pendingDetailsRequest || Time.time < nextDetailsAllowed || !CanLoadString())
            return;

        pendingDetailsRequest = false;
        detailsLoading = true;
        lastStringLoadTime = Time.time;
        VRCStringDownloader.LoadUrl(url_details, Receiver);
    }

    /// <summary>
    /// 取得済みの詳細からindexのイベントの詳細を反映する。詳細にそのIDが無ければfalse
    /// </summary>
    private bool ApplyDetails(int index)
    {
        if (detailsDict == null || ids[index] == null)
            return false;

        DataToken entry;
        if (!detailsDict.TryGetValue(ids[index], out entry) || entry.TokenType != TokenType.DataDictionary)
            return false;

        DataDictionary d = entry.DataDictionary;
        DataToken t;
        if (d.TryGetValue("body", out t) && t.TokenType == TokenType.String) bodys[index] = NeutralizeTags(t.String);
        if (d.TryGetValue("condition", out t) && t.TokenType == TokenType.String) conditions[index] = NeutralizeTags(t.String);
        if (d.TryGetValue("way", out t) && t.TokenType == TokenType.String) ways[index] = NeutralizeTags(t.String);
        if (d.TryGetValue("note", out t) && t.TokenType == TokenType.String) notes[index] = NeutralizeTags(t.String);
        return true;
    }

    private void OnDetailsLoaded(string json)
    {
        detailsLoading = false;

        DataToken root;
        DataToken detailsToken;
        if (string.IsNullOrEmpty(json)
            || !VRCJson.TryDeserializeFromJson(json, out root)
            || root.TokenType != TokenType.DataDictionary
            || !root.DataDictionary.TryGetValue("details", out detailsToken)
            || detailsToken.TokenType != TokenType.DataDictionary)
        {
            OnDetailsFailed();
            return;
        }

        detailFailCount = 0;
        detailsDict = detailsToken.DataDictionary;

        DataToken versionToken;
        detailsVersion = root.DataDictionary.TryGetValue("version", out versionToken)
            ? (versionToken.TokenType == TokenType.String ? versionToken.String : versionToken.ToString())
            : "";

        //詳細の方が一覧と違うversionなら、次の機会に一覧を取り直す(一度だけ)
        bool versionDiffers = detailsVersion != "" && listVersion != "" && detailsVersion != listVersion;
        if (versionDiffers && listRefetchFor != detailsVersion)
        {
            listRefetchFor = detailsVersion;
            nextEventsTicks = TicksAfter(STRING_LOAD_GAP);
        }

        //詳細ボックスを開いている場合は描き直す
        if (drawnIndex >= 0 && ContentImage != null && ContentImage.activeSelf)
        {
            if (ApplyDetails(drawnIndex))
            {
                pendingDetailId = null;
                DrawContent(drawnIndex, null);
            }
            else if (pendingDetailId != null)
            {
                pendingDetailId = null;
                DrawContent(drawnIndex, "更新中です。しばらくしてからもう一度開いてください");
            }
        }
    }

    /// <summary>
    /// 詳細の取得に失敗した場合: 30秒→60秒→…→最大300秒空けて再試行できるようにする
    /// </summary>
    private void OnDetailsFailed()
    {
        detailsLoading = false;
        detailFailCount++;

        float wait = 30f * Mathf.Pow(2f, detailFailCount - 1);
        if (wait > 300f)
            wait = 300f;
        nextDetailsAllowed = Time.time + wait;

        if (pendingDetailId != null && drawnIndex >= 0 && ContentImage != null && ContentImage.activeSelf)
        {
            pendingDetailId = null;
            DrawContent(drawnIndex, "読み込みに失敗しました。しばらくしてからもう一度開いてください");
        }
    }
    #endregion

    /// <summary>
    /// 取得失敗時: 取得済みの表示は残し、30秒→60秒→…→最大300秒の間隔で再試行する
    /// </summary>
    private void OnReloadFailed()
    {
        failCount++;
        hasJsonError = !hasData;
        eventsDue = true;

        float retry = 30f * Mathf.Pow(2f, failCount - 1);
        if (retry > 300f)
            retry = 300f;

        currentTime = 0f;
        reloadInterval = retry;

        //データがある場合は古い表示のまま、無い場合はローディングを残して再試行を待つ
        if (hasData)
            NOWLOADING.SetActive(false);
    }
    #endregion

    /// <summary>
    /// イベントの詳細情報を表示する際に呼ばれる関数
    /// </summary>
    /// <param name="id">表示するイベントのID</param>
    public void drawContentBox(string id)
    {
        if (ContentImage == null || ContentImage.activeSelf || hasJsonError)
        {
            DebugLog("Could not draw content box.");
            return;
        }

        //与えられたIDから本来のIndexを取得
        int index = getIndexbyID(id);

        if (index == -1)
        {
            DebugLog("Could not found index by id.");
            return;
        }

        //詳細を別に取得する形式の場合、まだ詳細が無ければ取得してから描画する
        if (NeedsDetailFetch())
        {
            if (!ApplyDetails(index))
            {
                pendingDetailId = id;
                pendingDetailsRequest = true;
                DrawContent(index, "読み込み中...");
                TryDispatchDetails();
                return;
            }

            //一覧より古いversionの詳細だった場合は、表示したうえで裏で一度だけ取り直す
            if (listVersion != "" && detailsVersion != listVersion && detailsRefreshedFor != listVersion)
            {
                detailsRefreshedFor = listVersion;
                pendingDetailsRequest = true;
            }
        }

        DrawContent(index, null);
    }

    /// <summary>
    /// indexのイベントの詳細ボックスを描画する。bodyMessageが指定されている場合は説明文の代わりに表示する
    /// </summary>
    private void DrawContent(int index, string bodyMessage)
    {
        drawnIndex = index;

        //一番上から表示したいので、イベントの詳細表示のボックスの位置を一番上に
        if (contentPanelRect != null)
            contentPanelRect.localPosition = new Vector3(0, 0, 0);

        //開催日
        _Day.text = dayStrs[index];

        //開催時間と終了時間を記載
        _Time.text = timeStrs[index];

        //Title
        _Title.text = $"<b>{titles[index]}</b>";

        //対応プラットフォームを記載
        _Quest.text = quests[index] ? "PC, Android" : "PC";

        //開催者を記載
        _Author.text = authors[index];

        //説明文を記載
        _Body.text = bodyMessage != null ? bodyMessage : bodys[index];

        //イベントのジャンルを記載
        string genreText = "";
        for (int genreIndex = 0; genreIndex < genres[index].Count; genreIndex++)
        {
            if (genres[index][genreIndex].TokenType != TokenType.String)
                continue;

            string _genre = (string)genres[index][genreIndex];

            if (!string.IsNullOrEmpty(_genre))
            {
                genreText = genreText + _genre;

                if (genreIndex != genres[index].Count - 1)
                    genreText = genreText + ", ";
            }
        }
        _Genre.text = genreText;

        //参加条件やモラルを記載
        _Conditions.text = conditions[index];

        //参加方法を記載
        _Way.text = ways[index];

        //ノートを記載
        _Note.text = notes[index];

        //詳細情報のボックスを表示
        ContentImage.SetActive(true);

        //詳細情報を見る際に後ろのスクロールも動いてしまうので、無効化
        _scrollRect.enabled = false;

        DebugLog($"Drawing {titles[index]}'s content");
    }

    /// <summary>
    /// イベントの詳細情報を閉じる際に呼ばれる関数
    /// </summary>
    public void closeContentBox()
    {
        if (ContentImage == null || !ContentImage.activeSelf)
            return;

        //取得待ちの詳細は取り消す(取得済みの詳細は残す)
        pendingDetailId = null;
        pendingDetailsRequest = false;
        drawnIndex = -1;

        //イベントの詳細情報のボックスを非表示
        ContentImage.SetActive(false);

        //裏のスクロールが動かないように制御していたのを直す
        _scrollRect.enabled = true;
    }

    /// <summary>
    /// クエスト対応のみを表示するToggleが変更されたときのイベント
    /// </summary>
    public void toggleAndroidMode()
    {
        ReloadEvents(questToggle.isOn, genreFilters);

        if (questToggle.isOn)
        {
            DebugLog($"Toggled on android mode");
        }
        else
        {
            DebugLog($"Toggled off android mode");
        }
    }

    public void CheckIsAndroid()
    {
#if UNITY_ANDROID
        questToggle.SetIsOnWithoutNotify(true);
#endif
    }

    public void GetEventFields()
    {
        //規定のポジションから開始
        Vector2 dayHeaderPosition = DefaultPositionDayRect;

        //見えない位置へ移動
        dayHeaderPosition.x = dayHeaderPosition.x + 1500;

        for (int i = 0; i < MAX_DAYHEADER_LENGTH; i++)
        {
            Transform dayHeader = Content.Find($"DayHeader_{i}");
            if (dayHeader == null)
            {
                DebugLog($"Could is not found DayHeaderObject [{i}]");
                continue;
            }

            //RectTransformとして取得 (as RectTransformは構文エラーになる)
            RectTransform dayHeaderRectTransform = dayHeader.GetComponent<RectTransform>();
            dayHeaders[i] = dayHeaderRectTransform;
            dayHeaders[i].localPosition = dayHeaderPosition;

            float dayScaleY = dayHeaderRectTransform.localScale.y;
            dayRectLo[i] = dayHeaderRectTransform.rect.yMin * dayScaleY;
            dayRectHi[i] = dayHeaderRectTransform.rect.yMax * dayScaleY;

            dayHeaderTexts[i] = dayHeaderRectTransform.GetComponentInChildren<Text>();
        }

        for (int i = 0; i < MAX_EVENT_LENGTH; i++)
        {
            Transform buttonTransform = Content.Find($"{i}");
            if (buttonTransform == null)
            {
                DebugLog($"Could is not found Button Object {i}");
                continue;
            }

            RectTransform buttonRectTransform = buttonTransform.GetComponent<RectTransform>();
            eventButtons[i] = buttonRectTransform;
            eventButtons[i].localPosition = dayHeaderPosition;

            float eventScaleY = buttonRectTransform.localScale.y;
            eventRectLo[i] = buttonRectTransform.rect.yMin * eventScaleY;
            eventRectHi[i] = buttonRectTransform.rect.yMax * eventScaleY;

            eventButtonTexts[i] = buttonRectTransform.Find("Button/Text").GetComponent<Text>();
            eventButtonTimeTexts[i] = buttonRectTransform.Find("TimeRect/TimeText").GetComponent<Text>();
            eventButtonScripts[i] = buttonTransform.GetComponentInChildren<eventcalendar_showcontent>();
        }

        for (int i = 0; i < MAX_GENRE_LENGTH; i++)
        {
            if (Filter == null)
            {
                DebugLog("Filter is not found");
                continue;
            }

            Transform checkbox = Filter.transform.Find($"{i}");
            if (checkbox == null)
            {
                DebugLog($"Genre checkbox gameObject is not found.");
                continue;
            }

            eventcalendar_checkbox _checkbox = checkbox.GetComponent<eventcalendar_checkbox>();
            if (_checkbox == null)
            {
                DebugLog($"checkbox eventcalendar_checkbox component is not found. {checkbox.name}");
                continue;
            }

            Text checkbox_text = checkbox.GetComponentInChildren<Text>();
            if (checkbox_text == null)
            {
                continue;
            }

            checkboxes[i] = _checkbox;
            checkboxTexts[i] = checkbox_text;
            checkboxes[i].gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// ジャンルフィルターを初期化する関数、基本的にフィルタのToggleが変わった際にしか呼ばれない
    /// </summary>
    public void InitializeFields()
    {
        genreFilters = new DataList();
    }

    /// <summary>
    /// ジャンルフィルターのToggleが変わった際に呼ばれる関数
    /// </summary>
    public void toggleFilter()
    {
        //リロード中は返す
        if (isReloading || isAllOFF)
            return;

        //フィルタを初期化
        InitializeFields();

        //一回ジャンルを整理
        RefleshGenres();

        //フィルタを元に表示
        ReloadEvents(questToggle.isOn, genreFilters);

        DebugLog($"Toggled filter [{genreFilters.Count}]");
    }

    /// <summary>
    /// 全てのジャンルフィルターのToggleを一括でOFFに
    /// </summary>
    public void toggleOFFFilters()
    {
        isAllOFF = true;

        if (Filter == null)
        {
            DebugLog("Filter is not found");
            return;
        }

        for (int i = 0; i < genres_filterlist.Count && i < MAX_GENRE_LENGTH; i++)
        {
            if (checkboxes[i] == null)
                continue;

            checkboxes[i].toggleisON(false);
        }

        isAllOFF = false;

        //更新をかける
        toggleFilter();
    }
    #endregion

    #region uGUI Func
    public void ViewScrolled()
    {
        if (rowCount == 0)
            return;

        //スクロールに追従しつつ、更新は0.1秒に1回までにする
        if (Time.time - lastVisibleUpdate >= VISIBLE_UPDATE_INTERVAL)
        {
            UpdateVisibleRows();
        }
        else if (!visibleUpdatePending)
        {
            visibleUpdatePending = true;
            SendCustomEventDelayedSeconds(nameof(DelayedVisibleUpdate), VISIBLE_UPDATE_INTERVAL, VRC.Udon.Common.Enums.EventTiming.Update);
        }
    }

    public void DelayedVisibleUpdate()
    {
        visibleUpdatePending = false;
        UpdateVisibleRows();
    }
    #endregion

    #region CharacterReference
    /// <summary>
    /// サーバーから受け取った文字列に '<' '>' があると、リッチテキストのタグとして解釈されてしまうため全角にする
    /// </summary>
    private string NeutralizeTags(string text)
    {
        if (text.IndexOf('<') < 0 && text.IndexOf('>') < 0)
            return text;

        return text.Replace("<", "＜").Replace(">", "＞");
    }

    private string ReplaceCharacterReference(string text)
    {
        if (text.IndexOf('&') < 0)
            return text;

        text = text.Replace("&copy;", "©");
        //'<' '>'に戻すとリッチテキストのタグとして解釈されてしまうため、全角に置き換える
        text = text.Replace("&lt;", "＜");
        text = text.Replace("&gt;", "＞");
        text = text.Replace("&amp;", "&");

        return text;
    }
    #endregion

#if UNITY_EDITOR && !COMPILER_UDONSHARP
    //---------------------------------------------------------------------------------------
    //Custom Inspector
    [CustomEditor(typeof(cuckoo_VRChatEventCalendar_v3))]
    public class cuckoo_VRChatEventCalender_v3_Editor : Editor
    {
        private Texture inspectorLogoTex;

        private SerializedProperty _reloadInterval;
        private SerializedProperty _useActiveRange;
        private SerializedProperty _activeRange;
        private SerializedProperty _NOWLOADING;
        private SerializedProperty _HeaderImage;
        private SerializedProperty _ContentImage;
        private SerializedProperty _questToggle;
        private SerializedProperty _mat_header;
        private SerializedProperty _url_header;
        private SerializedProperty _url_json;
        private SerializedProperty _url_details;
        private SerializedProperty _Content;
        private SerializedProperty _Receiver;
        private SerializedProperty _ScrollRect;
        private SerializedProperty _Filter;

        private SerializedProperty _ContentPanelRect;
        private SerializedProperty _Day, _Time, _Title, _Quest, _Author, _Body, _Genre, _Conditions, _Way, _Note;

        public bool showDetail = true;

        private void OnEnable()
        {
            _reloadInterval = serializedObject.FindProperty(nameof(reloadInterval));
            _useActiveRange = serializedObject.FindProperty(nameof(useActiveRange));
            _activeRange = serializedObject.FindProperty(nameof(activeRange));
            _NOWLOADING = serializedObject.FindProperty(nameof(NOWLOADING));
            _HeaderImage = serializedObject.FindProperty(nameof(HeaderImage));
            _ContentImage = serializedObject.FindProperty(nameof(ContentImage));
            _questToggle = serializedObject.FindProperty(nameof(questToggle));
            _mat_header = serializedObject.FindProperty(nameof(mat_header));
            _url_header = serializedObject.FindProperty(nameof(url_header));
            _url_json = serializedObject.FindProperty(nameof(url_json));
            _url_details = serializedObject.FindProperty(nameof(url_details));
            _Content = serializedObject.FindProperty(nameof(Content));
            _Receiver = serializedObject.FindProperty(nameof(Receiver));
            _ScrollRect = serializedObject.FindProperty(nameof(_scrollRect));
            _Filter = serializedObject.FindProperty(nameof(Filter));

            _ContentPanelRect = serializedObject.FindProperty("contentPanelRect");
            _Day = serializedObject.FindProperty("_Day");
            _Time = serializedObject.FindProperty("_Time");
            _Title = serializedObject.FindProperty("_Title");
            _Quest = serializedObject.FindProperty("_Quest");
            _Author = serializedObject.FindProperty("_Author");
            _Body = serializedObject.FindProperty("_Body");
            _Genre = serializedObject.FindProperty("_Genre");
            _Conditions = serializedObject.FindProperty("_Conditions");
            _Way = serializedObject.FindProperty("_Way");
            _Note = serializedObject.FindProperty("_Note");
        }
        private void DrawLogoTexture(string guid, string path)
        {
            if (inspectorLogoTex == null)
            {
                inspectorLogoTex = AssetDatabase.LoadAssetAtPath(path, typeof(Texture)) as Texture;

                if (inspectorLogoTex == null)
                {
                    inspectorLogoTex = AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(guid));

                    if (inspectorLogoTex == null)
                        return;
                }
            }
            float w = EditorGUIUtility.currentViewWidth;
            Rect rect = new Rect
            {
                width = w - 40f
            };
            rect.height = rect.width / 9.2f;
            Rect rect2 = GUILayoutUtility.GetRect(rect.width, rect.height);
            rect.x = ((EditorGUIUtility.currentViewWidth - rect.width) * 0.5f) - 4.0f;
            rect.y = rect2.y;
            GUI.DrawTexture(rect, inspectorLogoTex, ScaleMode.StretchToFill);
        }
        public override void OnInspectorGUI()
        {
            serializedObject.UpdateIfRequiredOrScript();

            EditorGUILayout.Space();
            EditorGUILayout.Space();

            DrawLogoTexture("3fd29cc1c649cff41ac6fe3d81087ade", "Packages/vrceve.calendar/Runtime/Resource/Images/InspectorView_Logo.png");

            EditorGUILayout.LabelField("Calendar Settings", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("初回読み込み以降は300に固定されます", EditorStyles.helpBox);
            EditorGUILayout.PropertyField(_reloadInterval, new GUIContent("読み込みインターバル"), true);

            EditorGUILayout.PropertyField(_useActiveRange, new GUIContent("範囲内でのみ更新する"), true);
            if (_useActiveRange.boolValue)
            {
                EditorGUILayout.LabelField("最初の1回は必ず読み込み、その後はこのオブジェクトの位置から範囲内にいる間だけ更新します(Quest等の負荷軽減)。初期値は ON / 5m です", EditorStyles.helpBox);
                EditorGUILayout.PropertyField(_activeRange, new GUIContent("範囲(メートル)"), true);
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space();
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Reference Settings", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_NOWLOADING, new GUIContent("Loading Texture"), true);
            EditorGUILayout.PropertyField(_ContentImage, new GUIContent("Event Detail Content"), true);
            EditorGUILayout.PropertyField(_questToggle, new GUIContent("Android Toggle"), true);
            EditorGUILayout.PropertyField(_mat_header, new GUIContent("Header Material"), true);
            EditorGUILayout.PropertyField(_HeaderImage, new GUIContent("Header Texture"), true);

            EditorGUILayout.PropertyField(_Content, new GUIContent("Content"), true);
            EditorGUILayout.PropertyField(_Receiver, new GUIContent("Receiver"), true);

            EditorGUILayout.PropertyField(_ScrollRect, new GUIContent("Scroll Rect"), true);
            EditorGUILayout.PropertyField(_Filter, new GUIContent("Filter"), true);

            EditorGUI.indentLevel--;
            EditorGUILayout.Space();
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("URLs", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_url_header, new GUIContent("Header Image URL"), true);
            EditorGUILayout.PropertyField(_url_json, new GUIContent("Json URL (一覧)"), true);
            EditorGUILayout.PropertyField(_url_details, new GUIContent("Details URL (詳細)"), true);
            EditorGUILayout.LabelField("Details URL が空の場合は、Json URL に含まれる詳細(旧形式)をそのまま使います", EditorStyles.helpBox);

            EditorGUI.indentLevel--;
            EditorGUILayout.Space();
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Content Panel", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_ContentPanelRect, new GUIContent("Content Panel"), true);
            EditorGUILayout.PropertyField(_Day, new GUIContent("Day"), true);
            EditorGUILayout.PropertyField(_Time, new GUIContent("Time"), true);
            EditorGUILayout.PropertyField(_Title, new GUIContent("Title"), true);
            EditorGUILayout.PropertyField(_Quest, new GUIContent("Android"), true);
            EditorGUILayout.PropertyField(_Author, new GUIContent("Author"), true);
            EditorGUILayout.PropertyField(_Body, new GUIContent("Body"), true);
            EditorGUILayout.PropertyField(_Genre, new GUIContent("Genre"), true);
            EditorGUILayout.PropertyField(_Conditions, new GUIContent("Conditions"), true);
            EditorGUILayout.PropertyField(_Way, new GUIContent("Way"), true);
            EditorGUILayout.PropertyField(_Note, new GUIContent("Note"), true);

            EditorGUI.indentLevel--;

            serializedObject.ApplyModifiedProperties();
        }
    }

    //---------------------------------------------------------------------------------------
    //MenuItem (Unityの上部メニュー)
    public class cuckoo_VRChatEventCalender_V3_Tool
    {
        private static void DebugLog(string msg = "", string color = "yellow", string title = "VRChatイベントカレンダー")
        {
            Debug.Log($"[<color={color}>{title}</color>]{msg}");
        }

        #region variable
        static string guid_prefab = "b9fbc0b476d953349adad81fe030abb3";
        #endregion

        #region Func
        private static bool isStringEmptyOrDontExists(string path)
        {
            return string.IsNullOrEmpty(path) || !System.IO.File.Exists(path);
        }

        private static GameObject GetPrefabFromGUID(string guid, string path = "")
        {
            string _path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(_path))
            {
                //guid、path両方から取得出来ない場合は返す
                if (isStringEmptyOrDontExists(path))
                    return null;

                _path = path;
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(_path);
        }
        #endregion

        [MenuItem("VRChatイベントカレンダー/プレハブ設置")]
        private static void SetupPrefab()
        {
            GameObject prefab = GetPrefabFromGUID(guid_prefab, "Packages/vrceve.calendar/Runtime/VRC_event_calendar v3.prefab");
            if (prefab == null)
            {
                DebugLog("PrefabがPackages内に存在しません。\nVRChatイベントカレンダーの再配置を行ってください。", "red");
                return;
            }

            GameObject panel = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            panel.name = prefab.name;
            EditorGUIUtility.PingObject(panel);

            DebugLog("設置しました！", "green");
        }
    }
#endif
}
