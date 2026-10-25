using System.Text.RegularExpressions;

namespace BarberSaas.Api.Services;

// Arabic/Hebrew versions of the `error` messages owners and customers can see. Controllers keep
// writing plain English; Filters/TranslateErrorFilter swaps the text when the request carries
// X-Lang: AR or HE (the frontend sends its current language). A message missing here simply stays
// English, so adding a new error never breaks anything -- add its translation when convenient.
// Platform-admin-only and internal messages are left out on purpose.
public static class ApiErrorTranslations
{
    // English -> (Arabic, Hebrew)
    private static readonly Dictionary<string, (string Ar, string He)> Exact = new()
    {
        // general
        ["Not found"] = ("غير موجود", "לא נמצא"),
        ["Unauthorized"] = ("غير مصرح", "אין הרשאה"),
        ["An unexpected error occurred."] = ("حدث خطأ غير متوقع.", "אירעה שגיאה לא צפויה."),
        ["Too many requests. Try again later"] = ("طلبات كثيرة جدًا. حاول لاحقًا", "יותר מדי בקשות. נסו שוב מאוחר יותר"),
        ["Invalid date"] = ("تاريخ غير صالح", "תאריך לא תקין"),
        ["Invalid date. Use the format YYYY-MM-DD."] = ("تاريخ غير صالح. استخدم الصيغة YYYY-MM-DD.", "תאריך לא תקין. יש להשתמש בפורמט YYYY-MM-DD."),
        ["Invalid request"] = ("طلب غير صالح", "בקשה לא תקינה"),
        ["Invalid week"] = ("أسبوع غير صالح", "שבוע לא תקין"),
        ["You can't block a date in the past"] = ("لا يمكن حجب تاريخ في الماضي", "אי אפשר לחסום תאריך שכבר עבר"),
        ["This date is already blocked"] = ("هذا التاريخ محجوب بالفعل", "התאריך הזה כבר חסום"),
        ["That name is reserved for the Default schedule"] = ("هذا الاسم محجوز للجدول الافتراضي", "השם הזה שמור ללוח הזמנים המוגדר כברירת מחדל"),
        ["This time is outside your working hours"] = ("هذا الوقت خارج ساعات عملك", "השעה הזו מחוץ לשעות הפעילות שלך"),
        ["This customer already has this appointment"] = ("هذا العميل لديه هذا الموعد بالفعل", "ללקוח הזה כבר יש את התור הזה"),
        ["The text contains characters that aren't allowed."] = ("يحتوي النص على أحرف غير مسموح بها.", "הטקסט מכיל תווים שאינם מותרים."),
        ["Invalid time"] = ("وقت غير صالح", "שעה לא תקינה"),

        // chats
        ["Write a message first"] = ("اكتب رسالة أولًا", "יש לכתוב הודעה קודם"),
        ["This message is too long"] = ("هذه الرسالة طويلة جدًا", "ההודעה ארוכה מדי"),
        ["This chat has ended"] = ("انتهت هذه المحادثة", "השיחה הזו הסתיימה"),
        ["Unknown message"] = ("رسالة غير معروفة", "הודעה לא מוכרת"),
        ["Add your services first"] = ("أضف خدماتك أولًا", "הוסיפו קודם את השירותים שלכם"),
        ["Choose a service"] = ("اختر خدمة", "בחרו שירות"),
        ["Choose a date"] = ("اختر تاريخًا", "בחרו תאריך"),
        ["Choose a date in the next 60 days"] = ("اختر تاريخًا خلال الـ 60 يومًا القادمة", "בחרו תאריך ב-60 הימים הקרובים"),
        ["Add your address in Settings first"] = ("أضف عنوانك في الإعدادات أولًا", "הוסיפו קודם את הכתובת בהגדרות"),
        ["You can save up to 100 ready messages"] = ("يمكنك حفظ حتى 100 رسالة جاهزة", "אפשר לשמור עד 100 הודעות מוכנות"),
        ["Write a name and a message"] = ("اكتب اسمًا ورسالة", "כתבו שם והודעה"),
        ["The name is too long"] = ("الاسم طويل جدًا", "השם ארוך מדי"),
        ["Send a photo (JPG, PNG, WEBP) or a PDF, Word or Excel file up to 10MB."] = ("أرسل صورة (JPG أو PNG أو WEBP) أو ملف PDF أو Word أو Excel حتى 10MB.", "שלחו תמונה (JPG,‏ PNG,‏ WEBP) או קובץ PDF,‏ Word או Excel עד 10MB."),
        ["This service can't be booked"] = ("لا يمكن حجز هذه الخدمة", "אי אפשר להזמין את השירות הזה"),
        ["Your WhatsApp bot isn't linked, so messages can't be sent"] = ("بوت واتساب غير مربوط، لذلك لا يمكن إرسال الرسائل", "בוט הוואטסאפ לא מחובר, ולכן אי אפשר לשלוח הודעות"),
        ["The message wasn't sent: the WhatsApp bot isn't connected right now"] = ("لم تُرسل الرسالة: بوت واتساب غير متصل حاليًا", "ההודעה לא נשלחה: בוט הוואטסאפ לא מחובר כרגע"),
        ["Invalid start time"] = ("وقت البدء غير صالح", "שעת התחלה לא תקינה"),
        ["Invalid day of week"] = ("يوم غير صالح", "יום לא תקין"),
        ["Please upload a JPG, PNG, or WEBP image up to 5MB."] = ("يرجى رفع صورة JPG أو PNG أو WEBP بحجم أقصاه 5MB.", "יש להעלות תמונת JPG,‏ PNG או WEBP עד 5MB."),

        // sign-in, codes, passwords
        ["Invalid email or password"] = ("البريد الإلكتروني أو كلمة المرور غير صحيحة", "אימייל או סיסמה שגויים"),
        ["Invalid or expired code"] = ("الرمز غير صالح أو منتهي الصلاحية", "הקוד שגוי או שפג תוקפו"),
        ["Invalid or expired verification code"] = ("رمز التحقق غير صالح أو منتهي الصلاحية", "קוד האימות שגוי או שפג תוקפו"),
        ["Please wait before requesting another code"] = ("يرجى الانتظار قبل طلب رمز آخر", "נא להמתין לפני בקשת קוד נוסף"),
        ["Password must be at least 6 characters"] = ("يجب أن تتكون كلمة المرور من 6 أحرف على الأقل", "הסיסמה חייבת להכיל לפחות 6 תווים"),
        ["Password must be at least 8 characters"] = ("يجب أن تتكون كلمة المرور من 8 أحرف على الأقل", "הסיסמה חייבת להכיל לפחות 8 תווים"),
        ["Current password is incorrect"] = ("كلمة المرور الحالية غير صحيحة", "הסיסמה הנוכחית שגויה"),
        ["Choose a password different from your current one"] = ("اختر كلمة مرور مختلفة عن الحالية", "בחרו סיסמה שונה מהסיסמה הנוכחית"),
        ["This account has been disabled. Contact support."] = ("تم تعطيل هذا الحساب. تواصل مع الدعم.", "החשבון הזה הושבת. פנו לתמיכה."),
        ["Your password was changed. Please sign in again."] = ("تم تغيير كلمة المرور. يرجى تسجيل الدخول مرة أخرى.", "הסיסמה שונתה. נא להתחבר שוב."),
        ["You must set a new password before continuing."] = ("يجب تعيين كلمة مرور جديدة قبل المتابعة.", "יש לבחור סיסמה חדשה לפני שממשיכים."),
        ["Please verify your email before signing in."] = ("يرجى تأكيد بريدك الإلكتروني قبل تسجيل الدخول.", "נא לאמת את האימייל לפני ההתחברות."),
        ["Please verify your email address first."] = ("يرجى تأكيد بريدك الإلكتروني أولًا.", "נא לאמת קודם את כתובת האימייל."),
        ["Email is already verified"] = ("البريد الإلكتروني مؤكد بالفعل", "האימייל כבר אומת"),
        ["Email and code are required"] = ("البريد الإلكتروني والرمز مطلوبان", "נדרשים אימייל וקוד"),
        ["Email already registered"] = ("البريد الإلكتروني مسجل بالفعل", "האימייל כבר רשום"),
        ["This email already has an account."] = ("لهذا البريد الإلكتروني حساب بالفعل.", "לאימייל הזה כבר יש חשבון."),
        ["This email already has an account. Try logging in instead."] = ("لهذا البريد الإلكتروني حساب بالفعل. جرّب تسجيل الدخول.", "לאימייל הזה כבר יש חשבון. נסו להתחבר."),
        ["Invalid email"] = ("بريد إلكتروني غير صالح", "אימייל לא תקין"),
        ["Could not send the verification email. Please try again shortly."] = ("تعذّر إرسال رسالة التحقق. حاول مرة أخرى بعد قليل.", "לא ניתן היה לשלוח את אימייל האימות. נסו שוב בעוד רגע."),
        ["Could not send the verification email. Please check the address and try again."] = ("تعذّر إرسال رسالة التحقق. تحقق من العنوان وحاول مرة أخرى.", "לא ניתן היה לשלוח את אימייל האימות. בדקו את הכתובת ונסו שוב."),
        ["Could not send the reset email. Please try again shortly."] = ("تعذّر إرسال رسالة إعادة التعيين. حاول مرة أخرى بعد قليل.", "לא ניתן היה לשלוח את אימייל האיפוס. נסו שוב בעוד רגע."),
        ["Phone and code are required"] = ("رقم الهاتف والرمز مطلوبان", "נדרשים מספר טלפון וקוד"),
        ["Token is required"] = ("الرمز مطلوب", "נדרש אסימון"),
        ["Invalid token"] = ("رمز غير صالح", "אסימון לא תקין"),
        ["This link has expired or is invalid."] = ("انتهت صلاحية هذا الرابط أو أنه غير صالح.", "פג תוקף הקישור או שהוא לא תקין."),
        ["This link has expired or is invalid"] = ("انتهت صلاحية هذا الرابط أو أنه غير صالح", "פג תוקף הקישור או שהוא לא תקין"),
        ["Name and family name required for new customers"] = ("الاسم واسم العائلة مطلوبان للعملاء الجدد", "שם ושם משפחה נדרשים ללקוחות חדשים"),

        // registration / account requests
        ["Business name is required"] = ("اسم العمل مطلوب", "נדרש שם עסק"),
        ["Business type is required"] = ("نوع العمل مطلوب", "נדרש סוג עסק"),
        ["Invalid business type"] = ("نوع عمل غير صالح", "סוג עסק לא תקין"),
        ["URL already taken"] = ("هذا الرابط مستخدم بالفعل", "הכתובת כבר תפוסה"),
        ["This URL is reserved"] = ("هذا الرابط محجوز", "הכתובת שמורה"),
        ["Slug must be lowercase letters, numbers and hyphens (3-60 characters)"] = ("يجب أن يتكون الرابط من أحرف إنجليزية صغيرة وأرقام وشرطات (3 إلى 60 حرفًا)", "הכתובת חייבת להכיל אותיות אנגליות קטנות, ספרות ומקפים (3 עד 60 תווים)"),
        ["First name and family name are required"] = ("الاسم الأول واسم العائلة مطلوبان", "נדרשים שם פרטי ושם משפחה"),
        ["First name and family name must be in English letters"] = ("يجب كتابة الاسم الأول واسم العائلة بأحرف إنجليزية", "שם פרטי ושם משפחה חייבים להיות באותיות אנגליות"),
        ["A request from this email is already pending review."] = ("يوجد طلب من هذا البريد قيد المراجعة بالفعل.", "בקשה מהאימייל הזה כבר ממתינה לבדיקה."),
        ["Please enter a valid phone number"] = ("يرجى إدخال رقم هاتف صالح", "נא להזין מספר טלפון תקין"),
        ["Phone is required"] = ("رقم الهاتف مطلوب", "נדרש מספר טלפון"),
        ["Payments are not yet configured. Please contact support."] = ("الدفع غير مُفعّل بعد. يرجى التواصل مع الدعم.", "התשלומים עדיין לא הוגדרו. נא לפנות לתמיכה."),
        ["Could not start checkout. Please try again."] = ("تعذّر بدء الدفع. حاول مرة أخرى.", "לא ניתן היה להתחיל את התשלום. נסו שוב."),

        // booking
        ["Slot not available"] = ("هذا الموعد غير متاح", "המועד לא פנוי"),
        ["Slot no longer available"] = ("هذا الموعد لم يعد متاحًا", "המועד כבר לא פנוי"),
        ["Booking unavailable"] = ("الحجز غير متاح", "ההזמנה לא זמינה"),
        ["This business isn't taking online bookings right now"] = ("هذه المصلحة لا تستقبل حجوزات عبر الإنترنت حاليًا", "העסק הזה לא מקבל כרגע הזמנות אונליין"),
        ["This item is not bookable"] = ("لا يمكن حجز هذه الخدمة", "לא ניתן להזמין את השירות הזה"),
        ["Item not found"] = ("الخدمة غير موجودة", "השירות לא נמצא"),
        ["Appointment not found"] = ("الموعد غير موجود", "התור לא נמצא"),
        ["Customer not found"] = ("العميل غير موجود", "הלקוח לא נמצא"),
        ["This appointment can no longer be modified"] = ("لم يعد بالإمكان تعديل هذا الموعد", "כבר לא ניתן לשנות את התור הזה"),
        ["This appointment is not currently booked"] = ("هذا الموعد غير محجوز حاليًا", "התור הזה לא מוזמן כרגע"),
        ["This time has changed. Please pick it again."] = ("تغيّر هذا الوقت. يرجى اختياره من جديد.", "השעה הזו השתנתה. בחרו אותה שוב."),
        ["This appointment has no cancellation request"] = ("لا يوجد طلب إلغاء لهذا الموعد", "אין בקשת ביטול לתור הזה"),
        ["This time overlaps an existing appointment"] = ("هذا الوقت يتداخل مع موعد موجود", "השעה הזו חופפת לתור קיים"),
        ["This time has already passed"] = ("هذا الوقت قد مضى", "השעה הזו כבר עברה"),
        ["The appointment must end by midnight"] = ("يجب أن ينتهي الموعد قبل منتصف الليل", "התור חייב להסתיים עד חצות"),
        ["This is your own appointment"] = ("هذا موعدك أنت", "זה התור שלך"),
        ["This business or item is no longer available"] = ("هذا العمل أو هذه الخدمة لم تعد متاحة", "העסק או השירות כבר לא זמינים"),
        ["Please log in to book."] = ("يرجى تسجيل الدخول للحجز.", "נא להתחבר כדי להזמין."),
        ["Please enter your name."] = ("يرجى إدخال اسمك.", "נא להזין את שמך."),
        ["Name is required"] = ("الاسم مطلوب", "נדרש שם"),
        ["Name must be at least 2 characters"] = ("يجب أن يتكون الاسم من حرفين على الأقل", "השם חייב להכיל לפחות 2 תווים"),
        ["Customer name and phone are required"] = ("اسم العميل ورقم هاتفه مطلوبان", "נדרשים שם וטלפון של הלקוח"),
        ["You've reached the maximum number of bookings allowed per day."] = ("وصلت إلى الحد الأقصى المسموح من الحجوزات في اليوم.", "הגעת למספר ההזמנות המרבי המותר ליום."),
        ["You've reached the maximum number of bookings allowed per week."] = ("وصلت إلى الحد الأقصى المسموح من الحجوزات في الأسبوع.", "הגעת למספר ההזמנות המרבי המותר לשבוע."),
        ["Only cancelling is supported"] = ("الإلغاء فقط مدعوم", "ניתן רק לבטל"),
        ["The selected photo is no longer available."] = ("الصورة المختارة لم تعد متاحة.", "התמונה שנבחרה כבר לא זמינה."),
        ["Invalid photo reference."] = ("مرجع صورة غير صالح.", "הפניה לתמונה לא תקינה."),
        ["Please upload a photo for this service."] = ("يرجى رفع صورة لهذه الخدمة.", "נא להעלות תמונה לשירות הזה."),
        ["Please choose a photo for this service."] = ("يرجى اختيار صورة لهذه الخدمة.", "נא לבחור תמונה לשירות הזה."),
        ["Please choose or upload a photo for this service."] = ("يرجى اختيار صورة أو رفعها لهذه الخدمة.", "נא לבחור או להעלות תמונה לשירות הזה."),
        ["This service doesn't use a reference photo."] = ("هذه الخدمة لا تستخدم صورة مرجعية.", "השירות הזה לא משתמש בתמונת השראה."),
        ["Invalid photo mode"] = ("وضع صورة غير صالح", "אפשרות תמונה לא תקינה"),
        ["Waitlist is not enabled for this business"] = ("قائمة الانتظار غير مفعّلة لهذا العمل", "רשימת ההמתנה לא פעילה בעסק הזה"),
        ["Waitlist entry not found"] = ("لم يتم العثور على الإدخال في قائمة الانتظار", "הרישום לרשימת ההמתנה לא נמצא"),

        // reviews
        ["Rating must be between 1 and 5"] = ("يجب أن يكون التقييم بين 1 و5", "הדירוג חייב להיות בין 1 ל-5"),
        ["You've already reviewed this business"] = ("لقد قيّمت هذا العمل بالفعل", "כבר כתבת ביקורת על העסק הזה"),
        ["You can review a business only after a completed appointment there."] = ("يمكنك تقييم عمل فقط بعد إتمام موعد فيه.", "אפשר לכתוב ביקורת על עסק רק אחרי תור שהושלם בו."),
        ["Reply cannot be empty"] = ("لا يمكن أن يكون الرد فارغًا", "התגובה לא יכולה להיות ריקה"),
        ["Reply must be 1000 characters or fewer"] = ("يجب ألا يتجاوز الرد 1000 حرف", "התגובה יכולה להכיל עד 1000 תווים"),

        // owner settings, services, schedule
        ["All name fields are required"] = ("جميع حقول الاسم مطلوبة", "כל שדות השם נדרשים"),
        ["Duration can be at most 24 hours"] = ("يمكن أن تكون المدة 24 ساعة كحد أقصى", "המשך יכול להיות עד 24 שעות"),
        ["Duration must be a multiple of 15 (min 15)"] = ("يجب أن تكون المدة من مضاعفات 15 (15 على الأقل)", "המשך חייב להיות כפולה של 15 (לפחות 15)"),
        ["Price must be between 0 and 1,000,000"] = ("يجب أن يكون السعر بين 0 و1,000,000", "המחיר חייב להיות בין 0 ל-1,000,000"),
        ["Language must be EN, AR or HE"] = ("يجب أن تكون اللغة EN أو AR أو HE", "השפה חייבת להיות EN,‏ AR או HE"),
        ["Chatbot default language must be EN, AR or HE"] = ("يجب أن تكون لغة الشات بوت الافتراضية EN أو AR أو HE", "שפת ברירת המחדל של הצ'אטבוט חייבת להיות EN,‏ AR או HE"),
        ["Max bookings per day must be between 1 and 1000, or empty for unlimited"] = ("يجب أن يكون الحد الأقصى للحجوزات في اليوم بين 1 و1000، أو فارغًا لعدم التقييد", "מספר ההזמנות המרבי ליום חייב להיות בין 1 ל-1000, או ריק ללא הגבלה"),
        ["Max bookings per week must be between 1 and 1000, or empty for unlimited"] = ("يجب أن يكون الحد الأقصى للحجوزات في الأسبوع بين 1 و1000، أو فارغًا لعدم التقييد", "מספר ההזמנות המרבי לשבוע חייב להיות בין 1 ל-1000, או ריק ללא הגבלה"),
        ["The cancellation-approval email address isn't valid"] = ("عنوان البريد الإلكتروني لموافقة الإلغاء غير صالح", "כתובת האימייל לאישור ביטולים לא תקינה"),
        ["The cancellation-approval WhatsApp number isn't a valid phone number"] = ("رقم واتساب لموافقة الإلغاء غير صالح", "מספר הוואטסאפ לאישור ביטולים לא תקין"),
        ["The inquiry email address isn't valid"] = ("عنوان البريد الإلكتروني للاستفسارات غير صالح", "כתובת האימייל לפניות לא תקינה"),
        ["The inquiry WhatsApp number isn't a valid phone number"] = ("رقم واتساب للاستفسارات غير صالح", "מספר הוואטסאפ לפניות לא תקין"),
        ["Map link must start with http:// or https://"] = ("يجب أن يبدأ رابط الخريطة بـ http:// أو https://", "קישור המפה חייב להתחיל ב-http://‏ או https://"),
        ["Map link can be at most 2000 characters"] = ("يمكن أن يصل رابط الخريطة إلى 2000 حرف كحد أقصى", "קישור המפה יכול להכיל עד 2000 תווים"),
        ["This time is already blocked"] = ("هذا الوقت محظور بالفعل", "השעה הזו כבר חסומה"),
        ["End time must be after start time"] = ("يجب أن يكون وقت الانتهاء بعد وقت البدء", "שעת הסיום חייבת להיות אחרי שעת ההתחלה"),
        ["Enter both a start and an end time, or block the full day"] = ("أدخل وقت البدء ووقت الانتهاء معًا، أو احجب اليوم بالكامل", "הזינו גם שעת התחלה וגם שעת סיום, או חסמו את כל היום"),
        ["End date cannot be before start date"] = ("لا يمكن أن يكون تاريخ الانتهاء قبل تاريخ البدء", "תאריך הסיום לא יכול להיות לפני תאריך ההתחלה"),
        ["Start date cannot be in the past"] = ("لا يمكن أن يكون تاريخ البدء في الماضي", "תאריך ההתחלה לא יכול להיות בעבר"),
        ["This date range has already ended"] = ("انتهى نطاق التواريخ هذا بالفعل", "טווח התאריכים הזה כבר הסתיים"),
        ["Range too large (max 366 days)"] = ("النطاق كبير جدًا (366 يومًا كحد أقصى)", "הטווח גדול מדי (עד 366 ימים)"),
        ["This preset is used by a pending or active scheduled change -- cancel that first."] = ("هذا الجدول مستخدم في تغيير مجدول قادم أو نشط — ألغِه أولًا.", "התבנית הזו בשימוש בשינוי מתוזמן ממתין או פעיל — בטלו אותו קודם."),
        ["The default schedule can't be deleted -- the system always needs a baseline."] = ("لا يمكن حذف الجدول الافتراضي — يحتاج النظام دائمًا إلى جدول أساسي.", "אי אפשר למחוק את לוח הזמנים הקבוע — המערכת תמיד צריכה בסיס."),
    };

    // Messages with a value inside. Each pattern captures the value as group 1; {0} marks where it goes.
    private static readonly (Regex En, string Ar, string He)[] Patterns =
    [
        (new(@"^Description can be at most (\d+) characters$"),
            "يمكن أن يتكون الوصف من {0} حرفًا كحد أقصى", "התיאור יכול להכיל עד {0} תווים"),
        (new(@"^City and address can be at most (\d+) characters$"),
            "يمكن أن تتكون المدينة والعنوان من {0} حرفًا كحد أقصى", "העיר והכתובת יכולות להכיל עד {0} תווים"),
        (new(@"^Reason can be at most (\d+) characters$"),
            "يمكن أن يتكون السبب من {0} حرفًا كحد أقصى", "הסיבה יכולה להכיל עד {0} תווים"),
        (new(@"^Names can be at most (\d+) characters\.$"),
            "يمكن أن تتكون الأسماء من {0} حرفًا كحد أقصى.", "שמות יכולים להכיל עד {0} תווים."),
        (new(@"^Notes can be at most (\d+) characters\.$"),
            "يمكن أن تتكون الملاحظات من {0} حرفًا كحد أقصى.", "הערות יכולות להכיל עד {0} תווים."),
        (new(@"^Chatbot messages can be at most (\d+) characters$"),
            "يمكن أن تتكون رسائل الشات بوت من {0} حرفًا كحد أقصى", "הודעות הצ'אטבוט יכולות להכיל עד {0} תווים"),
        (new(@"^Comment must be (\d+) characters or fewer$"),
            "يجب ألا يتجاوز التعليق {0} حرفًا", "התגובה יכולה להכיל עד {0} תווים"),
        (new(@"^This phone number already belongs to (.+)\. Search for them in the customer field instead\.$"),
            "رقم الهاتف هذا يخص {0} بالفعل. ابحث عنه في حقل العميل بدلًا من ذلك.", "מספר הטלפון הזה כבר שייך ל-{0}. חפשו אותו בשדה הלקוח."),
    ];

    // The lockout wait needs real plural forms (QA run 3: "بعد 8 دقيقة", "בעוד 1 דקות").
    private static readonly Regex SignInLockout = new(@"^Too many sign-in attempts\. Try again in (\d+) minutes?\.$");

    private static string Minutes(string lang, int n) => lang == "AR"
        ? n switch { 1 => "دقيقة واحدة", 2 => "دقيقتين", >= 3 and <= 10 => $"{n} دقائق", _ => $"{n} دقيقة" }
        : n switch { 1 => "דקה", 2 => "שתי דקות", _ => $"{n} דקות" };

    public static string Translate(string? lang, string message)
    {
        if (lang is not ("AR" or "HE")) return message;
        if (Exact.TryGetValue(message, out var t)) return lang == "AR" ? t.Ar : t.He;
        if (SignInLockout.Match(message) is { Success: true } lockout)
        {
            var wait = Minutes(lang, int.Parse(lockout.Groups[1].Value));
            return lang == "AR" ? $"محاولات تسجيل دخول كثيرة جدًا. حاول مرة أخرى بعد {wait}." : $"יותר מדי ניסיונות התחברות. נסו שוב בעוד {wait}.";
        }
        foreach (var (en, ar, he) in Patterns)
        {
            var m = en.Match(message);
            if (m.Success) return (lang == "AR" ? ar : he).Replace("{0}", m.Groups[1].Value);
        }
        return message;
    }
}
