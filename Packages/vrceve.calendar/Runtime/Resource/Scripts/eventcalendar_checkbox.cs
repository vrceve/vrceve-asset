
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

public class eventcalendar_checkbox : UdonSharpBehaviour
{
    public string filterName;
    private Toggle _cachedToggle;
    public cuckoo_VRChatEventCalendar_v3 _eventcalender;

    void Start()
    {
        
    }

    private Toggle GetToggle()
    {
        if (_cachedToggle == null)
            _cachedToggle = GetComponent<Toggle>();
        return _cachedToggle;
    }

    public void setEventFilter(string filter)
    {
        filterName = filter;
    }

    public void toggleisON(bool toggle)
    {
        Toggle _toggle = GetToggle();
        if (_toggle != null)
            _toggle.isOn = toggle;
    }

    public void toggleFilter()
    {
        if (GetToggle() != null)
            _eventcalender.toggleFilter();
    }

    public bool GetOn()
    {
        Toggle toggle = GetToggle();
        return toggle != null && toggle.isOn;
    }

    public string GetFilterName()
    {
        return filterName;
    }
}
