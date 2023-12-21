
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 見えていない部分を非表示にするスクリプト
/// </summary>
public class DeativeImage : UdonSharpBehaviour
{
    [SerializeField] RectTransform scrollViewRet;

    [HideInInspector]
    public RectTransform MyRect;

    [SerializeField] bool isDayHeader;

    Image myImage;
    Text dayText;

    Button button;
    Text buttonText;

    Image timeRect;
    Text timeText;

    public bool IsOverlapping(RectTransform rect1, RectTransform rect2)
    {
        var rect1Corners = new Vector3[4];
        var rect2Corners = new Vector3[4];

        rect1.GetWorldCorners(rect1Corners);
        rect2.GetWorldCorners(rect2Corners);

        for (var i = 0; i < 4; i++)
        {
            if (IsPointInsideRect(rect1Corners[i], rect2Corners))
            {
                return true;
            }

            if (IsPointInsideRect(rect2Corners[i], rect1Corners))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsPointInsideRect(Vector3 point, Vector3[] rectCorners)
    {
        var inside = false;

        //rectCornersの各頂点に対して、pointがrect内にあるかを確認
        for (int i = 0, j = 3; i < 4; j = i++)
        {
            if (((rectCorners[i].y > point.y) != (rectCorners[j].y > point.y)) &&
                (point.x < (rectCorners[j].x - rectCorners[i].x) * (point.y - rectCorners[i].y) / (rectCorners[j].y - rectCorners[i].y) + rectCorners[i].x))
            {
                inside = !inside;
            }
        }

        return inside;
    }

    public void ToggleImage(bool toggle)
    {
        myImage.enabled = toggle;
        if (isDayHeader)
        {
            dayText.enabled = toggle;
        }
        else
        {
            button.enabled = toggle;
            buttonText.enabled = toggle;

            timeRect.enabled = toggle;
            timeText.enabled = toggle;
        }
    }

    public void Loop()
    {
        bool _toggle = IsOverlapping(MyRect, scrollViewRet);
        ToggleImage(_toggle);

        SendCustomEventDelayedSeconds(nameof(Loop), 0.1f, VRC.Udon.Common.Enums.EventTiming.Update);
    }

    private void Start()
    {
        MyRect = GetComponent<RectTransform>();

        myImage = GetComponent<Image>();
        if (isDayHeader)
        {
            dayText = transform.Find("Text").GetComponent<Text>();
        }
        else
        {
            Transform t_button = transform.Find("Button");
            if (t_button != null)
            {
                button = t_button.GetComponent<Button>();
                buttonText = t_button.Find("Text").GetComponent<Text>();
            }

            Transform t_rect = transform.Find("TimeRect");
            if (t_rect != null)
            {
                timeRect = t_rect.GetComponent<Image>();
                timeText = t_rect.Find("TimeText").GetComponent<Text>();
            }

        }
        Loop();
    }
}
