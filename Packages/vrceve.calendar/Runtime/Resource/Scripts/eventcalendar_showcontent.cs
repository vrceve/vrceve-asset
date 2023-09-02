
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class eventcalendar_showcontent : UdonSharpBehaviour
{
    private string eventID;
    public cuckoo_VRChatEventCalendar_v3 _eventcalender;

    private void Start()
    {
        
    }

    public void setEventID(string id)
    {
        eventID = id;
        //Debug.Log(eventID);
    }

    public void drawContent()   
    {
        if (_eventcalender != null && !string.IsNullOrEmpty(eventID))
        {
            _eventcalender.drawContentBox(eventID);
        }
    }
}
