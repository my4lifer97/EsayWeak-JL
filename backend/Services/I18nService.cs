namespace BarberSaas.Api.Services;

public static class I18nService
{
    private static readonly Dictionary<string, Dictionary<string, string>> Messages = new()
    {
        ["EN"] = new()
        {
            ["reminder.message"] = "Hi {customerName}! Reminder: your appointment with {businessName} is tomorrow at {time} for {service}.\n\nTo cancel: {cancelUrl}",
            ["reminder.message.soon"] = "Hi {customerName}! Reminder: your appointment with {businessName} is today at {time} for {service}, starting in a few hours.\n\nTo cancel: {cancelUrl}",
            ["whatsapp.selectService"] = "Hi! I'm {businessName}'s booking assistant. Which service would you like?\n\n{list}\n\nReply with the number.",
            // Same tail as whatsapp.selectService minus the greeting -- used after a business's own
            // custom welcome message, which replaces the greeting instead of stacking on top of it.
            ["whatsapp.selectServicePrompt"] = "Which service would you like?\n\n{list}\n\nReply with the number.",
            ["whatsapp.invalidServiceSelection"] = "Sorry, I didn't recognize that. Please reply with just the number of the service you'd like.",
            ["whatsapp.tooManyInvalidReplies"] = "Looks like you're not sending what was asked. If you'd like to continue chatting with me, send \"{unlockKeyword}\".",
            ["whatsapp.bookingConfirmed"] = "You're booked! Hi {customerName}, your {service} appointment with {businessName} is confirmed for {date} at {time}. See you then!",
            ["whatsapp.noServices"] = "Sorry, {businessName} hasn't set up any bookable services yet.",
            ["whatsapp.serviceLinkSent"] = "Great! Book your {service} appointment here: {url}",
            ["whatsapp.cancelled"] = "Your appointment on {date} at {time} has been cancelled. ✓",
            ["whatsapp.noAppointment"] = "I couldn't find an upcoming appointment for your number. Let's book one instead.",
            ["whatsapp.rescheduleIntro"] = "To reschedule, let's book a new appointment.",
            ["whatsapp.waitlistSlotOpen"] = "Hi {customerName}! A slot with {businessName} on {date} at {time} for {service} just opened up. Book it here (first come, first served): {url}",
            ["whatsapp.ownerCancellationApprovalNeeded"] = "Hi! {customerName} just cancelled their appointment on {date} at {time} for {service}. It's on hold until you decide what to do with it — open your dashboard to offer it to the waitlist, cancel it, or assign someone else: {url}",
        },
        ["AR"] = new()
        {
            ["reminder.message"] = "مرحبًا {customerName}! تذكير: موعدك مع {businessName} غدًا الساعة {time} لخدمة {service}.\n\nللإلغاء: {cancelUrl}",
            ["reminder.message.soon"] = "مرحبًا {customerName}! تذكير: موعدك مع {businessName} اليوم الساعة {time} لخدمة {service}، يبدأ خلال بضع ساعات.\n\nللإلغاء: {cancelUrl}",
            ["whatsapp.selectService"] = "مرحبًا! أنا مساعد حجز {businessName}. ما هي الخدمة التي تريدها؟\n\n{list}\n\nأرسل الرقم للاختيار.",
            ["whatsapp.selectServicePrompt"] = "ما هي الخدمة التي تريدها؟\n\n{list}\n\nأرسل الرقم للاختيار.",
            ["whatsapp.invalidServiceSelection"] = "عذرًا، لم أفهم ذلك. الرجاء إرسال رقم الخدمة فقط.",
            ["whatsapp.tooManyInvalidReplies"] = "يبدو أنك لا ترسل ما طُلب منك. إذا كنت ترغب في متابعة الدردشة معي، أرسل \"{unlockKeyword}\".",
            ["whatsapp.bookingConfirmed"] = "تم الحجز! مرحبًا {customerName}، تم تأكيد موعد {service} مع {businessName} بتاريخ {date} الساعة {time}. أراك حينها!",
            ["whatsapp.noServices"] = "عذرًا، لم يقم {businessName} بإعداد أي خدمات للحجز بعد.",
            ["whatsapp.serviceLinkSent"] = "رائع! احجز موعد {service} هنا: {url}",
            ["whatsapp.cancelled"] = "تم إلغاء موعدك في {date} الساعة {time}. ✓",
            ["whatsapp.noAppointment"] = "لم أجد موعدًا قادمًا لرقمك. لنحجز موعدًا جديدًا.",
            ["whatsapp.rescheduleIntro"] = "لإعادة الجدولة، لنحجز موعدًا جديدًا.",
            ["whatsapp.waitlistSlotOpen"] = "مرحبًا {customerName}! أصبح هناك موعد متاح مع {businessName} في {date} الساعة {time} لخدمة {service}. احجزه هنا (الأسبقية للأسرع): {url}",
            ["whatsapp.ownerCancellationApprovalNeeded"] = "مرحبًا! قام {customerName} للتو بإلغاء موعده في {date} الساعة {time} لخدمة {service}. الموعد معلّق حتى تقرر ماذا تفعل به — افتح لوحة التحكم لعرضه على قائمة الانتظار أو إلغائه أو تعيين شخص آخر: {url}",
        },
        ["HE"] = new()
        {
            ["reminder.message"] = "שלום {customerName}! תזכורת: התור שלך אצל {businessName} מחר בשעה {time} לשירות {service}.\n\nלביטול: {cancelUrl}",
            ["reminder.message.soon"] = "שלום {customerName}! תזכורת: התור שלך אצל {businessName} היום בשעה {time} לשירות {service}, מתחיל בעוד כמה שעות.\n\nלביטול: {cancelUrl}",
            ["whatsapp.selectService"] = "שלום! אני עוזר התורים של {businessName}. איזה שירות תרצה?\n\n{list}\n\nהשב עם המספר.",
            ["whatsapp.selectServicePrompt"] = "איזה שירות תרצה?\n\n{list}\n\nהשב עם המספר.",
            ["whatsapp.invalidServiceSelection"] = "סליחה, לא הבנתי. אנא שלח רק את מספר השירות שתרצה.",
            ["whatsapp.tooManyInvalidReplies"] = "נראה שאתה לא שולח את מה שביקשתי. אם תרצה להמשיך לשוחח איתי, שלח \"{unlockKeyword}\".",
            ["whatsapp.bookingConfirmed"] = "התור נקבע! שלום {customerName}, התור שלך ל{service} אצל {businessName} אושר לתאריך {date} בשעה {time}. נתראה!",
            ["whatsapp.noServices"] = "סליחה, {businessName} עדיין לא הגדיר שירותים לקביעת תור.",
            ["whatsapp.serviceLinkSent"] = "מעולה! קבע תור ל{service} כאן: {url}",
            ["whatsapp.cancelled"] = "התור שלך ב-{date} בשעה {time} בוטל. ✓",
            ["whatsapp.noAppointment"] = "לא מצאתי תור קרוב למספר שלך. בוא נקבע תור חדש.",
            ["whatsapp.rescheduleIntro"] = "לשינוי תור, בוא נקבע תור חדש.",
            ["whatsapp.waitlistSlotOpen"] = "שלום {customerName}! התפנה תור אצל {businessName} בתאריך {date} בשעה {time} לשירות {service}. קבע אותו כאן (הראשון שמזמין זוכה): {url}",
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
