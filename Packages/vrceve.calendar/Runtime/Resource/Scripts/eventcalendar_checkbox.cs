
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

public class eventcalendar_checkbox : UdonSharpBehaviour
{
    public string filterName;
    public cuckoo_VRChatEventCalendar_v3 _eventcalender;

    void Start()
    {
        
    }

    public void setEventFilter(string filter)
    {
        filterName = filter;
    }

    public void toggleisON(bool toggle)
    {
        Toggle _toggle = this.GetComponent<Toggle>();
        if (_toggle != null)
            _toggle.isOn = toggle;
    }

    public void toggleFilter()
    {
        Toggle toggle = this.GetComponent<Toggle>();
        if (toggle != null)
            _eventcalender.toggleFilter();
    }

    public bool GetOn()
    {
        Toggle toggle = this.GetComponent<Toggle>();
        return toggle.isOn;
    }

    public string GetFilterName()
    {
        return filterName;
    }
}
