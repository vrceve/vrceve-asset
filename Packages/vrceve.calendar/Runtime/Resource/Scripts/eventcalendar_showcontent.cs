
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class eventcalendar_showcontent : UdonSharpBehaviour
{
    public string eventID;
    public cuckoo_VRChatEventCalendar_v3 _eventcalender;

    public void setEventID(string id)
    {
        eventID = id;
    }

    public void drawContent()   
    {
        if (_eventcalender != null && !string.IsNullOrEmpty(eventID))
        {
            _eventcalender.drawContentBox(eventID);
        }
    }
}
