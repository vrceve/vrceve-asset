
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

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class cuckoo_VRChatEventCalendar_v3 : UdonSharpBehaviour
{
    /// <summary>
    /// Udon Debugに出力する時に見やすいように
    /// </summary>
    /// <param name="text"></param>
    private void DebugLog(string text)
    {
        Debug.Log($"[<color=yellow>VRChatEventCalender_v3</color>]{text}");
    }

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
    private bool isJsonError;

    /// <summary>
    /// 上部に表示される画像データのダウンロードに失敗した際にTrueになるフラグ
    /// </summary>
    private bool isHeaderError;

    /// <summary>
    /// データのリロードまでのインターバル、2回目以降から300に固定される
    /// </summary>
    public float reloadInterval = 7f;

    /// <summary>
    /// 前回のリロードからの経過時間
    /// </summary>
    public float currentTime = 0f;

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

    /// <summary>
    /// ダウンロードした際の結果
    /// </summary>
    private IVRCImageDownload _task;

    /// <summary>
    /// イベントのリストを表示する最初のポジション
    /// ここからどんどん足してく
    /// </summary>
    Vector2 DefaultPositionDayRect = new Vector2(491f, -25.0f);
 
    RectTransform[] dayHeaders = new RectTransform[10];
    RectTransform[] eventButtons = new RectTransform[256];
    eventcalendar_showcontent[] eventButtonScripts = new eventcalendar_showcontent[256];

    Text[] dayHeaderTexts = new Text[10];
    Text[] eventButtonTexts = new Text[256];

    Text[] eventButtonTimeTexts = new Text[256];

    int checkboxLen = 0;
    eventcalendar_checkbox[] checkboxes = new eventcalendar_checkbox[16];
    Text[] checkboxTexts = new Text[16];

    bool isOverlapChecking = false;
    float checkOverlapCooltime = 0.0f;
    #endregion

    #region Internal Func
    /// <summary>
    /// スクリプト開始時
    /// </summary>
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

        //ループ開始
        UpdateLoop();
    }

    /// <summary>
    /// 破棄時の挙動 (あるかわかんない)
    /// </summary>
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
        //読み込み (初回のみ7秒)
        currentTime += Time.deltaTime;
        if (currentTime > reloadInterval)
        {
            reloadCal();
        }

        #region Overlap CheckLoop
        if (checkOverlapCooltime > 0)
            checkOverlapCooltime -= Time.deltaTime;

        if (0 >= checkOverlapCooltime && isOverlapChecking)
        {
            for (int i = 0; i < 10; i++)
            {
                if (dayHeaders[i] == null)
                    continue;

                dayHeaders[i].gameObject.SetActive(IsOverlapping(dayHeaders[i], _scrollRect.viewport));
                //dayHeaders[i].gameObject.SetActive(IsVisibleInViewport(dayHeaders[i], _scrollRect.viewport));
            }

            for (int i = 0; i < 256; i++)
            {
                if (eventButtons[i] == null)
                    continue;

                eventButtons[i].gameObject.SetActive(IsOverlapping(eventButtons[i], _scrollRect.viewport));
                //eventButtons[i].gameObject.SetActive(IsVisibleInViewport(eventButtons[i], _scrollRect.viewport));
            }

            checkOverlapCooltime = 0.3f;
            isOverlapChecking = false;
        }
        #endregion

        SendCustomEventDelayedSeconds(nameof(UpdateLoop), 0.01f);
    }
    #endregion

    /// <summary>
    /// クリック時に更新
    /// </summary>
    public override void Interact()
    {
        currentTime = reloadInterval + 1;
        reloadStep = 0;
    }

    #region Optimize Func

    public static Rect GetWorldRect(RectTransform rectTransform)
    {
        Vector3[] corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);

        Vector3 position = corners[0];

        Vector2 size = new Vector2(
            rectTransform.lossyScale.x * rectTransform.rect.size.x,
            rectTransform.lossyScale.y * rectTransform.rect.size.y);

        return new Rect(position, size);
    }

    public bool IsOverlapping(RectTransform rectTransform1, RectTransform rectTransform2)
    {
        Rect rect1 = GetWorldRect(rectTransform1);
        Rect rect2 = GetWorldRect(rectTransform2);

        return rect1.Overlaps(rect2);
    }
 
    #endregion

    #region DateTime Func
    /// <summary>
    /// 与えられたjsonからDateTimeに変換
    /// </summary>
    /// <param name="timeStr"></param>
    /// <param name="time"></param>
    private void GetTimeFromStr(string timeStr, out DateTime time)
    {
        int _Plus = timeStr.IndexOf('+');
        if (_Plus == -1)
        {
            DebugLog("Plus is not found");
            time = new DateTime();
            return;
        }

        //日付を変換
        string _time = timeStr.Substring(0, _Plus);
        time = DateTime.ParseExact(_time, format, null);
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
        genres = new DataList[count];
    }

    /// <summary>
    /// 日付からその日のイベントデータだけを返す
    /// </summary>
    /// <param name="dateStr">日付</param>
    /// <returns></returns>
    private DataList getIDsbyDate(string dateStr)
    {
        DataList ret = new DataList();
        for (int i = 0; i < start_times.Length; i++)
        {
            //string startTimeStr = start_times[i].ToString("yyyy-MM-dd (ddd)");
            string startTimeStr = DateTimeToString(start_times[i]);

            if (string.IsNullOrEmpty(startTimeStr))
            {
                DebugLog($"startTimeStr is empty. {i}");
                continue;
            }

            if (dateStr == startTimeStr)
                ret.Add(ids[i]);
        }

        return ret;
    }

    private string DateTimeToString(DateTime time)
    {
        if (time == null)
            return "";

        string timeStr = time.ToString("yyyy-MM-dd");

        if (time.DayOfWeek == DayOfWeek.Monday)
            timeStr += " (月)";
        else if (time.DayOfWeek == DayOfWeek.Tuesday)
            timeStr += " (火)";
        else if (time.DayOfWeek == DayOfWeek.Wednesday)
            timeStr += " (水)";
        else if (time.DayOfWeek == DayOfWeek.Thursday)
            timeStr += " (木)";
        else if (time.DayOfWeek == DayOfWeek.Friday)
            timeStr += " (金)";
        else if (time.DayOfWeek == DayOfWeek.Saturday)
            timeStr += " (土)";
        else if (time.DayOfWeek == DayOfWeek.Sunday)
            timeStr += " (日)";

        return timeStr;
    }
    #endregion

    /// <summary>
    /// 更新時に呼ばれる関数、更新時だけNOWLOADINGを表示
    /// </summary>
    public void reloadCal()
    {
        //エラーが起きた場合は再リロードさせる
        DebugLog("Calling reload.");
        if (reloadStep == 0)
        {
            isReloading = true;

            //詳細情報を表示するボックスが表示されている場合は非表示
            closeContentBox();

            DebugLog("Reset Button Position.");
            ResetButton();

            //位置をリセット

            DebugLog("Reset Event Datas.");
            InitializeEventDatas(0);

            NOWLOADING.SetActive(true);

            //一度非表示にしないとマテリアルが反映されないため？ 非表示
            HeaderImage.SetActive(false);

            // ダウンロード実行
            _task = imgDownloader.DownloadImage(
                url_header,
                mat_header,
                Receiver, 
                info);

            foreach (var dayHeader in dayHeaders)
                dayHeader.gameObject.SetActive(false);

            foreach (var eventButton in eventButtons)
                eventButton.gameObject.SetActive(false);

            //タイトルを取得
            VRCStringDownloader.LoadUrl(url_json, Receiver);

            //処理をdeltaTime分ずらすため？
            reloadStep = 1;
        }
        else if (reloadStep == 1)
        {
            //非表示にしたヘッダー用オブジェクトを表示
            HeaderImage.SetActive(true);

            //Cal.SetActive(true);
            DebugLog("Reloaded.");

            //タイマーをリセット
            reloadStep = 0;
            currentTime = 0;
            reloadInterval = 60 * 5; //初回以降は５分に１回更新
        }
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
    /// イベントデータを日付によって分ける
    /// </summary>
    /// <param name="isQuest">クエスト対応のみ分けるか</param>
    /// <param name="genreFilters">ジャンルフィルター</param>
    /// <returns></returns>
    private DataList splitArrayByDate(bool isQuest = false, DataList genreFilters = null)
    {
        //DataListの中に日付ごとにIDを分ける
        //DataListの中に入るDataTokenには日付のstringとIDを入れる

        //なのでまず何日分あるかを判定する
        DataList dayStrings = new DataList();
        for (int i = 0; i < start_times.Length; i++)
        {
            //string dayStr = start_times[i].ToString("yyyy-MM-dd (ddd)");
            string dayStr = DateTimeToString(start_times[i]);
            if (string.IsNullOrEmpty(dayStr))
            {
                DebugLog($"dayStr is empty. {i}");
                continue;
            }

            if (genreFilters.Count > 0)
            {
                if (!checkGenre(genres[i], genreFilters))
                {
                    continue;
                }
            }
            else
                continue;

            if (isQuest && !quests[i])
                continue;

            if (dayStrings.IndexOf(dayStr) == -1)
            {
                dayStrings.Add(dayStr);
            }
        }

        int dayCount = dayStrings.Count;

        //0以下の場合は完全に取得出来ていないためそのまま返す
        if (dayCount < 1)
        {
            return null;
        }

        /*
         * DataList 
         *      [0]
         *       |---- 日付のデータ
         *       |---- EventIDのリスト
         *      [1]
         *       |---- 日付のデータ
         *       |---- EventIDのリスト
        */

        //IDを抱えるためのDataListを作る
        DataList splitedArray = new DataList();
        for (int i = 0; i < dayCount; i++)
        {
            string dayStr = dayStrings[i].ToString();
            DataList idList = getIDsbyDate(dayStr);

            DataList splited = new DataList();
            splited.Add(dayStr);
            splited.Add(idList);

            splitedArray.Add(splited);
        }

        return splitedArray;
    }

    /// <summary>
    /// ボタンの位置を初期位置へ(見えないところへ移動してる)
    /// </summary>
    private void ResetButton()
    {
        //ポジションをリセット
        RectTransform RectTransformContent = Content.GetComponent<RectTransform>();

        if (RectTransformContent != null)
            RectTransformContent.localPosition = new Vector3(5, 0, -1);

        //規定のポジションから開始
        Vector2 dayHeaderPosition = DefaultPositionDayRect;

        //見えない位置へ移動
        dayHeaderPosition.x = dayHeaderPosition.x + 1500;

        for (int i = 0; i < 10; i++)
        {
            if (dayHeaders[i] == null)
                continue;

            dayHeaders[i].localPosition = dayHeaderPosition;
            dayHeaders[i].gameObject.SetActive(false);
        }

        for (int i = 0; i < 256; i++)
        {
            if (eventButtons[i] == null)
                continue;

            eventButtons[i].localPosition = dayHeaderPosition;
            eventButtons[i].gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 分けられたデータを元にイベントのリストを作成 ボタンにIDを入れる
    /// </summary>
    /// <param name="datalist"></param>
    /// <param name="isQuest"></param>
    /// <param name="genrefilters"></param>
    private void CreateButtonFromDataList(DataList datalist, bool isQuest = false, DataList genrefilters = null)
    {
        //リセットする
        ResetButton();

        if (datalist == null)
            return;

        float rectHeight = DefaultPositionDayRect.y;

        float rectSizeY = 0.0f;

        int rectNum = 0;

        int loopCount = datalist.Count > 10 ? 10 : datalist.Count;

        for (int i = 0; i < loopCount; i++)
        {
            if (i >= datalist.Count)
                break;

            DataList dayToken = datalist[i].DataList;
            DataList IDList = (DataList)dayToken[1];

            rectSizeY -= 45;

            for (int j = 0; j < IDList.Count; j++)
            {
                string id = (string)IDList[j];
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                int index = getIndexbyID(id);
                if (index == -1)
                {
                    continue;
                }

                if (genrefilters.Count > 0)
                {
                    if (!checkGenre(genres[index], genrefilters))
                    {
                        continue;
                    }
                }

                if (isQuest && !quests[index])
                {
                    continue;
                }

                rectSizeY -= 30;
            }

            rectSizeY -= 10;
        }

        Vector2 test = _scrollRect.content.sizeDelta;
        test.y = rectSizeY * -1;

        _scrollRect.content.sizeDelta = test;

        //日付を表示するヘッダーを10個しか用意してないので10
        for (int i = 0; i < loopCount; i++)
        {
            if (i >= datalist.Count)
                break;

            /*
             * DataList 
             *      [0]
             *       |---- 日付のデータ
             *       |---- EventIDのリスト
             *      [1]
             *       |---- 日付のデータ
             *       |---- EventIDのリスト
            */
        
            DataList dayToken = datalist[i].DataList;
            string DayStr = (string)dayToken[0];
            DataList IDList = (DataList)dayToken[1];

            //規定のポジションから開始
            Vector2 dayHeaderPosition = DefaultPositionDayRect;

            //高さだけ計算済みの物に置き換え
            dayHeaderPosition.y = rectHeight;

            dayHeaders[i].localPosition = dayHeaderPosition;

            if (dayHeaderTexts[i] != null)
                dayHeaderTexts[i].text = DayStr;
            
            //Rect分引く
            rectHeight -= 45;

            for (int j = 0; j < IDList.Count; j++)
            {
                string id = (string)IDList[j];
                if (string.IsNullOrEmpty(id))
                {
                    DebugLog($"empty id detected i = {i}, j = {j}");
                    continue;
                }

                int index = getIndexbyID(id);
                if (index == -1)
                {
                    DebugLog($"unknown id loaded {id}");
                    continue;
                }

                if (genrefilters.Count > 0)
                {
                    if (!checkGenre(genres[index], genrefilters))
                    {
                        //DebugLog($"Skipped {titles[index]} [Filter Out].");
                        continue;
                    }
                }

                if (isQuest && !quests[index])
                {
                    continue;
                }

                //次のために追加しておく
 
                RectTransform rectTransform = eventButtons[rectNum];
                if (rectTransform != null)
                {
                    //高さだけ計算済みの物に置き換え
                    dayHeaderPosition.y = rectHeight;

                    rectTransform.localPosition = dayHeaderPosition;
                }

                rectHeight -= 30;

                eventcalendar_showcontent button = eventButtonScripts[rectNum];
                button.setEventID(id);

                Text _ButtonText = eventButtonTexts[rectNum];
                if (_ButtonText != null)
                {
                    _ButtonText.text = titles[index];
                }

                if (eventButtonTimeTexts[rectNum] != null)
                {
                    DateTime start = start_times[index];
                    DateTime end = end_times[index];

                    TimeSpan span = end - start;

                    string format = "HH:mm";
                    DateTime endTime = start + span;

                    eventButtonTimeTexts[rectNum].text = $"{start.ToString(format)} ~ {endTime.ToString(format)}";
                }

                eventButtons[rectNum].gameObject.SetActive(true);
                rectNum++;
            }

            rectHeight -= 10;

            eventButtons[rectNum].gameObject.SetActive(false);
        }
        isOverlapChecking = true;
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
        CreateButtonFromDataList(splitedArr, isQuest, genreFilters);
    }

    /// <summary>
    /// 受け取ったjsonデータを元に各配列へデータを分ける
    /// </summary>
    /// <param name="events">受け取ったデータ</param>
    private void DeserializeEvents(DataList events)
    {
        InitializeEventDatas(events.Count);
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i].TokenType == TokenType.String)
            {
                DebugLog($"str = {events[i].String}");
            }
            else if (events[i].TokenType == TokenType.DataDictionary)
            {
                var dics = events[i].DataDictionary;

                var keys = dics.GetKeys();
                var vals = dics.GetValues();

                for (int j = 0; j < dics.Count; j++)
                {
                    var key = keys[j];
                    var val = vals[j];

                    if (val.TokenType == TokenType.String)
                    {
                        if (key.String == "id")
                        {
                            ids[i] = val.String;
                        }
                        else if (key.String == "title")
                        {
                            titles[i] = val.String;
                        }
                        else if (key.String == "author")
                        {
                            authors[i] = val.String;
                        }
                        else if (key.String == "body")
                        {
                            bodys[i] = val.String;
                        }
                        else if (key.String == "condition")
                        {
                            conditions[i] = val.String;
                        }
                        else if (key.String == "way")
                        {
                            ways[i] = val.String;
                        }
                        else if (key.String == "note")
                        {
                            notes[i] = val.String;
                        }
                        else if (key.String == "start")
                        {
                            string starttime = val.String;

                            DateTime _start;
                            GetTimeFromStr(starttime, out _start);
                            if (_start == null)
                            {
                                DebugLog($"<color=red>Get Time Error start[{j}]</color>");
                                continue;
                            }

                            start_times[i] = _start;
                        }
                        else if (key.String == "end")
                        {
                            string endtime = val.String;

                            DateTime _end;
                            GetTimeFromStr(endtime, out _end);
                            if (_end == null)
                            {
                                DebugLog($"<color=red>Get Time Error end[{j}]</color>");
                                continue;
                            }

                            end_times[i] = _end;
                        }
                    }
                    else if (val.TokenType == TokenType.DataList) //たぶんgenres用
                    {
                        if (key.String == "genres")
                        {
                            genres[i] = val.DataList;
                        }
                        else
                        {
                            DebugLog($"Unknown DataList detected {j}");
                        }
                    }
                    else if (val.TokenType == TokenType.Boolean)
                    {
                        if (key.String == "quest")
                        {
                            quests[i] = val.Boolean;
                        }
                    }
                }
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
        for (int i = 0; i < 16; i++)
        {
            if (checkboxes[i] == null)
                continue;

            checkboxes[i].gameObject.SetActive(false);
        }

        checkboxLen = genres_filterlist.Count;
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

        for (int i = 0; i < genres_filterlist.Count; i++)
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
        if (VRCJson.TryDeserializeFromJson(json, out DataToken result))
        {
            //だいたいDictionaryで返ってくる
            if (result.TokenType == TokenType.DataDictionary)
            {
                var keys = result.DataDictionary.GetKeys();
                var vals = result.DataDictionary.GetValues();

                for (int i = 0; i < keys.Count; i++)
                {
                    if (keys[i].TokenType == TokenType.String)
                    {
                        var val = vals[i];
                        if (keys[i].String == "genres")
                        {
                            if (val.TokenType == TokenType.DataDictionary)
                            {
                                DataDictionary _genres = val.DataDictionary;
                                if (_genres == null)
                                    continue;

                                DeserializeGenres(_genres);
                            }
                        }

                        if (keys[i].String == "events")
                        {   
                            if (val.TokenType == TokenType.DataList)
                            {
                                DeserializeEvents(val.DataList);
                            }
                        }
                    }
                }
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
        isHeaderError = false;

        //受け取ったテクスチャをマテリアルへ反映
        mat_header.mainTexture = result.Result;
    }

    /// <summary>
    /// ヘッダーイメージを読み込んだ際に失敗した場合
    /// </summary>
    /// <param name="result"></param>
    public override void OnImageLoadError(IVRCImageDownload result)
    {
        isHeaderError = true;

        var error = result.Error;
        var errorMessage = result.ErrorMessage;

        DebugLog(error.ToString());
        DebugLog(errorMessage.ToString());
    }

    /// <summary>
    /// イベントデータのダウンロードに成功した場合
    /// </summary>
    /// <param name="result"></param>
    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        isJsonError = false;

        if (!string.IsNullOrEmpty(result.Result))
            DeserializeJson(result.Result);
    }

    /// <summary>
    /// イベントデータのダウンロードに失敗した場合
    /// </summary>
    /// <param name="result"></param>
    public override void OnStringLoadError(IVRCStringDownload result)
    {
        isJsonError = true;
        DebugLog(result.Error);
        DebugLog(result.ErrorCode.ToString());
    }
    #endregion

    /// <summary>
    /// イベントの詳細情報を表示する際に呼ばれる関数
    /// </summary>
    /// <param name="id">表示するイベントのID</param>
    public void drawContentBox(string id)
    {
        if (ContentImage == null || ContentImage.activeSelf || isJsonError)
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

        //一番上から表示したいので、イベントの詳細表示のボックスの位置を一番上に
        if (contentPanelRect != null)
            contentPanelRect.localPosition = new Vector3(0, 0, 0);

        DateTime start = start_times[index];
        DateTime end = end_times[index];

        //開催日を記載
        #region Day
        string startStr = DateTimeToString(start);
        _Day.text = startStr;
        #endregion

        //開催時間と終了時間を記載
        #region Time
        string format = "HH:mm";

        _Time.text = $"{start.ToString(format)} ~ {end.ToString(format)}";
        #endregion

        #region Title
        _Title.text = $"<b>{titles[index]}</b>";
        #endregion

        //対応プラットフォームを記載
        #region Quest
        _Quest.text = quests[index] ? "PC, Android" : "PC";
        #endregion

        //開催者を記載
        #region Author
        _Author.text = authors[index];
        #endregion

        //説明文を記載
        #region Body
        _Body.text = bodys[index];
        #endregion

        //イベントのジャンルを記載
        #region Genre
        //イベントのジャンルを記載

        _Genre.text = "";
        for (int j = 0; j < genres[index].Count; j++)
        {
            if (genres[index][j].TokenType != TokenType.String)
                continue;

            string _genre = (string)genres[index][j];

            if (!string.IsNullOrEmpty(_genre))
            {
                _Genre.text = _Genre.text + _genre;

                if (j != genres[index].Count - 1)
                    _Genre.text = _Genre.text + ", ";
            }
        }
        #endregion

        //参加条件やモラルを記載
        #region Conditions
        _Conditions.text = conditions[index];
        #endregion

        //参加方法を記載
        #region Way
        _Way.text = ways[index];
        #endregion

        //ノートを記載
        #region Note
        _Note.text = notes[index];
        #endregion

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

        //イベントの詳細情報のボックスを非表示
        ContentImage.SetActive(false);

        //裏のスクロールが動かないように制御していたのを直す
        _scrollRect.enabled = true;
    }

    /// <summary>
    /// クエスト対応のみを表示するToggleが変更されたときのイベント
    /// </summary>
    public void toggleQuestMode()
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

    public void GetEventFields()
    {
        //規定のポジションから開始
        Vector2 dayHeaderPosition = DefaultPositionDayRect;

        //見えない位置へ移動
        dayHeaderPosition.x = dayHeaderPosition.x + 1500;

        for (int i = 0; i < 10; i++)
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

            dayHeaderTexts[i] = dayHeaderRectTransform.GetComponentInChildren<Text>();
        }

        for (int i = 0; i < 256; i++)
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

            eventButtonTexts[i] = buttonRectTransform.Find("Button/Text").GetComponent<Text>();
            eventButtonTimeTexts[i] = buttonRectTransform.Find("TimeRect/TimeText").GetComponent<Text>();
            eventButtonScripts[i] = buttonTransform.GetComponentInChildren<eventcalendar_showcontent>();
        }

        for (int i = 0; i < 16; i++)
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
        //とりあえずクリア
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

        for (int i = 0; i < genres_filterlist.Count; i++)
        {
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
        isOverlapChecking = true;
    }
    #endregion

#if UNITY_EDITOR && !COMPILER_UDONSHARP
    [CustomEditor(typeof(cuckoo_VRChatEventCalendar_v3))]
    public class cuckoo_VRChatEventCalender_v3_Editor : UnityEditor.Editor
    {
        private Texture InspectorView_Logo_Texture;
        private const string InspectorView_Logo_guid = "3fd29cc1c649cff41ac6fe3d81087ade";


        private SerializedProperty _reloadInterval;
        private SerializedProperty _NOWLOADING;
        private SerializedProperty _HeaderImage;
        private SerializedProperty _ContentImage;
        private SerializedProperty _questToggle;
        private SerializedProperty _mat_header;
        private SerializedProperty _url_header;
        private SerializedProperty _url_json;
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
            _NOWLOADING = serializedObject.FindProperty(nameof(NOWLOADING));
            _HeaderImage = serializedObject.FindProperty(nameof(HeaderImage));
            _ContentImage = serializedObject.FindProperty(nameof(ContentImage));
            _questToggle = serializedObject.FindProperty(nameof(questToggle));
            _mat_header = serializedObject.FindProperty(nameof(mat_header));
            _url_header = serializedObject.FindProperty(nameof(url_header));
            _url_json = serializedObject.FindProperty(nameof(url_json));
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
        private void DrawLogoTexture(string guid, Texture texture)
        {
            if (texture == null)
            {
                texture = AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(guid));
                return;
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
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill);
        }
        public override void OnInspectorGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            InspectorView_Logo_Texture = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(InspectorView_Logo_guid), typeof(Texture)) as Texture;
            DrawLogoTexture(InspectorView_Logo_guid, InspectorView_Logo_Texture);

            EditorGUILayout.LabelField("Calendar Settings", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("初回読み込み以降は300に固定されます", EditorStyles.helpBox);
            EditorGUILayout.PropertyField(_reloadInterval, new GUIContent("読み込みの遅延秒数"), true);

            EditorGUI.indentLevel--;
            EditorGUILayout.Space();
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Reference Settings", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_NOWLOADING, true);
            EditorGUILayout.PropertyField(_HeaderImage, true);
            EditorGUILayout.PropertyField(_ContentImage, true);
            EditorGUILayout.PropertyField(_questToggle, true);
            EditorGUILayout.PropertyField(_mat_header, true);

            EditorGUILayout.PropertyField(_Content, true);
            EditorGUILayout.PropertyField(_Receiver, true);

            EditorGUILayout.PropertyField(_ScrollRect, true);
            EditorGUILayout.PropertyField(_Filter, true);

            EditorGUI.indentLevel--;
            EditorGUILayout.Space();
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("URLs", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_url_header, true);
            EditorGUILayout.PropertyField(_url_json, true);

            EditorGUI.indentLevel--;
            EditorGUILayout.Space();
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Content Panel", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_ContentPanelRect, true);
            EditorGUILayout.PropertyField(_Day, true);
            EditorGUILayout.PropertyField(_Time, true);
            EditorGUILayout.PropertyField(_Title, true);
            EditorGUILayout.PropertyField(_Quest, true);
            EditorGUILayout.PropertyField(_Author, true);
            EditorGUILayout.PropertyField(_Body, true);
            EditorGUILayout.PropertyField(_Genre, true);
            EditorGUILayout.PropertyField(_Conditions, true);
            EditorGUILayout.PropertyField(_Way, true);
            EditorGUILayout.PropertyField(_Note, true);

            EditorGUI.indentLevel--;
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Udon Settings", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            UdonSharpGUI.DrawConvertToUdonBehaviourButton(target);
            UdonSharpGUI.DrawProgramSource(target);
            UdonSharpGUI.DrawSyncSettings(target);
            UdonSharpGUI.DrawUtilities(target);
            serializedObject.ApplyModifiedProperties();
            EditorGUI.indentLevel--;
        }
    }

    public class cuckoo_VRChatEventCalender_V3_Tool : EditorWindow
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

            GameObject panel = Instantiate(prefab);
            panel.name = prefab.name;
            EditorGUIUtility.PingObject(panel);

            DebugLog("設置しました！", "green");
        }
    }
#endif
}
