namespace BarberSaas.Api.Services;

public static class I18nService
{
    private static readonly Dictionary<string, Dictionary<string, string>> Messages = new()
    {
        ["EN"] = new()
        {
            ["reminder.message"] = "Hi {customerName}! Reminder: your appointment with {barberName} is tomorrow at {time} for {service}.\n\nTo cancel: {cancelUrl}",
            ["whatsapp.bookingLink"] = "Hi! Book your appointment here: {url}",
            ["whatsapp.menu"] = "Hi! I'm {barberName}'s booking assistant.\n\nReply:\nBOOK - to book an appointment\nCANCEL - to cancel your appointment\nRESCHEDULE - to change your appointment",
            ["whatsapp.cancelled"] = "Your appointment on {date} at {time} has been cancelled. ✓",
            ["whatsapp.noAppointment"] = "I couldn't find an upcoming appointment for your number. To book: {url}",
            ["whatsapp.rescheduleLink"] = "To reschedule, book a new appointment here: {url}",
            ["whatsapp.waitlistSlotOpen"] = "Hi {customerName}! A slot with {barberName} on {date} at {time} for {service} just opened up. Book it here (first come, first served): {url}",
            ["whatsapp.ownerCancellationApprovalNeeded"] = "Hi! {customerName} just cancelled their appointment on {date} at {time} for {service}. It's on hold until you decide what to do with it — open your dashboard to offer it to the waitlist, cancel it, or assign someone else: {url}",
        },
        ["AR"] = new()
        {
            ["reminder.message"] = "مرحبًا {customerName}! تذكير: موعدك مع {barberName} غدًا الساعة {time} لخدمة {service}.\n\nللإلغاء: {cancelUrl}",
            ["whatsapp.bookingLink"] = "مرحبًا! احجز موعدك هنا: {url}",
            ["whatsapp.menu"] = "مرحبًا! أنا مساعد حجز {barberName}.\n\nأرسل:\nموعد - لحجز موعد\nإلغاء - لإلغاء موعدك\nتغيير - لتغيير موعدك",
            ["whatsapp.cancelled"] = "تم إلغاء موعدك في {date} الساعة {time}. ✓",
            ["whatsapp.noAppointment"] = "لم أجد موعدًا قادمًا لرقمك. للحجز: {url}",
            ["whatsapp.rescheduleLink"] = "لإعادة الجدولة، احجز موعدًا جديدًا هنا: {url}",
            ["whatsapp.waitlistSlotOpen"] = "مرحبًا {customerName}! أصبح هناك موعد متاح مع {barberName} في {date} الساعة {time} لخدمة {service}. احجزه هنا (الأسبقية للأسرع): {url}",
            ["whatsapp.ownerCancellationApprovalNeeded"] = "مرحبًا! قام {customerName} للتو بإلغاء موعده في {date} الساعة {time} لخدمة {service}. الموعد معلّق حتى تقرر ماذا تفعل به — افتح لوحة التحكم لعرضه على قائمة الانتظار أو إلغائه أو تعيين شخص آخر: {url}",
        },
        ["HE"] = new()
        {
            ["reminder.message"] = "שלום {customerName}! תזכורת: התור שלך אצל {barberName} מחר בשעה {time} לשירות {service}.\n\nלביטול: {cancelUrl}",
            ["whatsapp.bookingLink"] = "שלום! קבע תור כאן: {url}",
            ["whatsapp.menu"] = "שלום! אני עוזר התורים של {barberName}.\n\nשלח:\nשריין - לקביעת תור\nביטול - לביטול התור\nשינוי - לשינוי התור",
            ["whatsapp.cancelled"] = "התור שלך ב-{date} בשעה {time} בוטל. ✓",
            ["whatsapp.noAppointment"] = "לא מצאתי תור קרוב למספר שלך. לקביעת תור: {url}",
            ["whatsapp.rescheduleLink"] = "לשינוי תור, קבע תור חדש כאן: {url}",
            ["whatsapp.waitlistSlotOpen"] = "שלום {customerName}! התפנה תור אצל {barberName} בתאריך {date} בשעה {time} לשירות {service}. קבע אותו כאן (הראשון שמזמין זוכה): {url}",
            ["whatsapp.ownerCancellationApprovalNeeded"] = "שלום! {customerName} זה עתה ביטל את התור בתאריך {date} בשעה {time} לשירות {service}. התור מוקפא עד שתחליט מה לעשות איתו — פתח את לוח הבקרה כדי להציע אותו לרשימת ההמתנה, לבטל אותו, או לשייך מישהו אחר: {url}",
        },
    };

    public static string T(string lang, string key, Dictionary<string, string>? args = null)
    {
        var messages = Messages.GetValueOrDefault(lang) ?? Messages["EN"];
        var template = messages.GetValueOrDefault(key) ?? key;
        if (args is null) return template;
        return args.Aggregate(template, (s, kv) => s.Replace($"{{{kv.Key}}}", kv.Value));
    }
}
