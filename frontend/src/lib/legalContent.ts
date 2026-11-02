// Text of the /terms, /privacy and /accessibility pages, per language. Kept out of i18n.ts because
// it is long prose, not UI strings. This is a starting draft: have it reviewed by an Israeli lawyer
// before relying on it, and update LEGAL_UPDATED whenever the text changes.

export type LegalDoc = 'terms' | 'privacy' | 'accessibility'
type Lang = 'EN' | 'AR' | 'HE'
type Section = { h: string; p: string[] }
type DocText = { title: string; intro: string; sections: Section[] }

export const LEGAL_CONTACT_EMAIL = 'esayweekpasskey@gmail.com'
export const LEGAL_UPDATED = '2026-11-02'

const E = LEGAL_CONTACT_EMAIL

const content: Record<Lang, Record<LegalDoc, DocText>> = {
  HE: {
    terms: {
      title: 'תנאי שימוש',
      intro: 'התנאים האלה חלים על כל שימוש באתר ובשירות EsayWeek ("השירות"), גם של בעלי עסקים וגם של לקוחות שקובעים תורים. השימוש בשירות מהווה הסכמה לתנאים.',
      sections: [
        { h: 'מה השירות עושה', p: ['EsayWeek היא מערכת לקביעת תורים לעסקי שירות: דף עסק, קביעת תורים אונליין, תזכורות, רשימת המתנה וצ\'אטבוט בוואטסאפ.', 'התור עצמו הוא התקשרות בין הלקוח לבין העסק. EsayWeek אינה צד לשירות שהעסק נותן, למחיר שלו או לאיכות שלו.'] },
        { h: 'חשבון בעל עסק', p: ['חשבון עסק נפתח לאחר אישור בקשה. בעל העסק אחראי לנכונות הפרטים שמסר, לשמירה על הסיסמה ולכל פעולה שנעשית בחשבון.', 'בעל העסק אחראי לכך שיש לו רשות לשמור את פרטי הלקוחות שלו ולשלוח להם הודעות, ולכך שהשימוש שלו עומד בדין, כולל דיני הגנת הפרטיות ודיני דיוור.'] },
        { h: 'תשלום', p: ['השירות ניתן בתשלום חודשי, לאחר תקופת ניסיון אם הוצעה. המחיר ואופן התשלום נקבעים בעת ההצטרפות או בתיאום איתנו. אי תשלום עלול להביא להשהיית החשבון.'] },
        { h: 'חיבור וואטסאפ', p: ['הצ\'אטבוט פועל דרך מספר וואטסאפ שבעל העסק מחבר בעצמו (כמו וואטסאפ ווב), ולא דרך הממשק העסקי הרשמי של WhatsApp. WhatsApp עלולה להגביל או לחסום מספר שמחובר כך. בעל העסק מבין את הסיכון ומקבל אותו, ו-EsayWeek אינה אחראית לחסימה או לאובדן גישה למספר.'] },
        { h: 'שימוש אסור', p: ['אסור להשתמש בשירות לשליחת דואר זבל, לתוכן פוגעני או לא חוקי, להתחזות, לפגיעה באבטחת המערכת או לגישה לנתונים של אחרים.'] },
        { h: 'זמינות ואחריות', p: ['השירות ניתן כפי שהוא (AS IS). אנחנו עושים מאמץ שיפעל ברציפות, אבל לא מתחייבים שלא יהיו תקלות, הפסקות או אובדן הודעות.', 'ככל שהדין מתיר, האחריות של EsayWeek מוגבלת לסכום ששולם עבור השירות בשלושת החודשים שלפני האירוע, ואיננו אחראים לנזק עקיף, לאובדן רווח או לתור שהוחמץ.'] },
        { h: 'סיום', p: ['בעל עסק יכול להפסיק את השימוש בכל עת. אנחנו רשאים להשהות או לסגור חשבון שמפר את התנאים או שלא שולם.'] },
        { h: 'שינויים ודין', p: ['ייתכן שנעדכן את התנאים. תאריך העדכון האחרון מופיע בראש הדף. על התנאים חל הדין הישראלי, וסמכות השיפוט לבתי המשפט המוסמכים בישראל.'] },
        { h: 'יצירת קשר', p: [`שאלות על התנאים: ${E}`] },
      ],
    },
    privacy: {
      title: 'מדיניות פרטיות',
      intro: 'הדף מסביר איזה מידע EsayWeek אוספת, למה, עם מי הוא משותף ואילו זכויות יש לך.',
      sections: [
        { h: 'מי אחראי על המידע', p: ['על פרטי בעלי העסקים ועל האתר EsayWeek אחראית.', 'על פרטי הלקוחות של עסק (תורים, הודעות, תמונות) אחראי העסק עצמו, ו-EsayWeek מחזיקה ומעבדת אותם עבורו. לבקשות לגבי מידע כזה אפשר לפנות לעסק או אלינו.'] },
        { h: 'איזה מידע נאסף', p: ['בעלי עסקים: שם, אימייל, טלפון, פרטי העסק, שירותים ומחירים, שעות פעילות, וסיסמה (שמורה מוצפנת).', 'לקוחות: שם, מספר טלפון, תורים והערות, תמונות שהועלו לתור, ביקורות, והודעות וקבצים שנשלחו לצ\'אטבוט של העסק בוואטסאפ.', 'מידע טכני: כתובת IP ופעולות כניסה ושינוי (לאבטחה ולמניעת ניצול לרעה).'] },
        { h: 'למה', p: ['כדי לקבוע ולנהל תורים, לשלוח אישורים, תזכורות והודעות על שינויים, להפעיל את הצ\'אטבוט, לאבטח את המערכת ולתת תמיכה. איננו מוכרים מידע ואיננו משתמשים בו לפרסום.'] },
        { h: 'עם מי המידע משותף', p: ['עם העסק שאצלו קבעת תור.', 'עם ספקים שמפעילים את השירות עבורנו: אחסון בענן (Railway, שרתים שעשויים להיות מחוץ לישראל), שליחת אימיילים (Brevo, Google), WhatsApp להעברת הודעות, ספק סליקה כשיופעל תשלום באשראי, ואם יופעל, ספק בינה מלאכותית (OpenAI) להבנת הודעות בצ\'אט.', 'וכאשר הדין מחייב זאת.'] },
        { h: 'כמה זמן נשמר', p: ['קבצים שנשלחו בצ\'אט נמחקים אחרי 90 יום. שאר המידע נשמר כל עוד החשבון של העסק פעיל, או כפי שהדין מחייב.'] },
        { h: 'אחסון בדפדפן', p: ['האתר שומר בדפדפן את פרטי ההתחברות, השפה וערכת הצבעים שבחרת. אין עוגיות פרסום או מעקב.'] },
        { h: 'אבטחה', p: ['החיבור מוצפן (HTTPS), הסיסמאות שמורות כגיבוב, והגישה לנתונים מוגבלת. אין מערכת חסינה לגמרי, ואם יתגלה אירוע אבטחה שנוגע לך נודיע כפי שהדין מחייב.'] },
        { h: 'הזכויות שלך', p: ['לפי חוק הגנת הפרטיות, התשמ"א-1981, אפשר לבקש לעיין במידע עליך, לתקן אותו או למחוק אותו. אפשר גם לבקש להפסיק לקבל הודעות. בקשות: ' + E + '.'] },
        { h: 'קטינים', p: ['השירות אינו מיועד לפתיחת חשבון עסק על ידי מי שמתחת לגיל 18.'] },
        { h: 'שינויים', p: ['נעדכן את הדף כשמשהו משתנה. תאריך העדכון האחרון מופיע בראש הדף.'] },
      ],
    },
    accessibility: {
      title: 'הצהרת נגישות',
      intro: 'אנחנו רוצים שכל אחד יוכל לקבוע תור ולנהל עסק ב-EsayWeek, כולל אנשים עם מוגבלות.',
      sections: [
        { h: 'רמת הנגישות', p: ['היעד שלנו הוא התאמה לתקן הישראלי 5568 ולהנחיות WCAG 2.0 ברמה AA. האתר עדיין לא עבר בדיקת נגישות מלאה על ידי מורשה נגישות, ולכן ייתכן שחלקים ממנו אינם נגישים במלואם.'] },
        { h: 'מה כבר קיים', p: ['תמיכה בעברית, ערבית ואנגלית, כולל כיוון כתיבה מימין לשמאל.', 'מצב כהה ומצב בהיר.', 'התאמה למסכי טלפון ולהגדלת טקסט בדפדפן.', 'הודעות שגיאה מוצגות כטקסט.'] },
        { h: 'מגבלות ידועות', p: ['חלק מהכפתורים מבוססי אייקון בלבד, בחלק מהטפסים לשדות יש רק טקסט מציין מקום, ולוח השנה ולוח הזמנים של בעל העסק עדיין לא נבדקו עם קורא מסך. אנחנו עובדים על שיפור.'] },
        { h: 'נתקלת בבעיה?', p: [`נשמח לשמוע ולעזור, כולל קביעת תור בדרך אחרת. רכז הנגישות: ${E}. אנחנו משתדלים לענות תוך 5 ימי עבודה.`] },
      ],
    },
  },
  AR: {
    terms: {
      title: 'شروط الاستخدام',
      intro: 'تنطبق هذه الشروط على كل استخدام لموقع وخدمة EsayWeek ("الخدمة")، من أصحاب الأعمال ومن الزبائن الذين يحجزون مواعيد. استخدام الخدمة يعني الموافقة على الشروط.',
      sections: [
        { h: 'ما الذي تقدمه الخدمة', p: ['EsayWeek نظام لحجز المواعيد لأعمال الخدمات: صفحة للعمل، حجز أونلاين، تذكيرات، قائمة انتظار وروبوت محادثة في واتساب.', 'الموعد نفسه اتفاق بين الزبون والعمل. EsayWeek ليست طرفًا في الخدمة التي يقدمها العمل ولا في سعرها أو جودتها.'] },
        { h: 'حساب صاحب العمل', p: ['يُفتح حساب العمل بعد الموافقة على الطلب. صاحب العمل مسؤول عن صحة البيانات وعن حماية كلمة المرور وعن كل ما يتم في الحساب.', 'صاحب العمل مسؤول عن أن لديه إذنًا بحفظ بيانات زبائنه ومراسلتهم، وعن أن استخدامه يتوافق مع القانون، بما في ذلك قوانين حماية الخصوصية والرسائل.'] },
        { h: 'الدفع', p: ['الخدمة باشتراك شهري، بعد فترة تجربة إن عُرضت. السعر وطريقة الدفع يُحددان عند الانضمام أو بالتنسيق معنا. عدم الدفع قد يؤدي إلى إيقاف الحساب.'] },
        { h: 'ربط واتساب', p: ['يعمل روبوت المحادثة عبر رقم واتساب يربطه صاحب العمل بنفسه (مثل واتساب ويب)، وليس عبر واجهة WhatsApp الرسمية للأعمال. قد تقيّد WhatsApp أو تحظر رقمًا مربوطًا بهذه الطريقة. صاحب العمل يفهم هذا الخطر ويقبله، وEsayWeek غير مسؤولة عن الحظر أو فقدان الوصول إلى الرقم.'] },
        { h: 'الاستخدام الممنوع', p: ['يُمنع استخدام الخدمة لإرسال رسائل مزعجة أو محتوى مسيء أو غير قانوني أو لانتحال الشخصية أو للمساس بأمان النظام أو للوصول إلى بيانات الآخرين.'] },
        { h: 'التوفر والمسؤولية', p: ['تُقدَّم الخدمة كما هي (AS IS). نبذل جهدنا لتعمل باستمرار، لكننا لا نضمن عدم حدوث أعطال أو انقطاع أو فقدان رسائل.', 'بالقدر الذي يسمح به القانون، تقتصر مسؤولية EsayWeek على المبلغ المدفوع مقابل الخدمة في الأشهر الثلاثة السابقة للحدث، ولسنا مسؤولين عن ضرر غير مباشر أو خسارة ربح أو موعد فائت.'] },
        { h: 'الإنهاء', p: ['يمكن لصاحب العمل التوقف عن الاستخدام في أي وقت. يحق لنا إيقاف أو إغلاق حساب يخالف الشروط أو لم يُدفع.'] },
        { h: 'التغييرات والقانون', p: ['قد نحدّث الشروط. تاريخ آخر تحديث يظهر في أعلى الصفحة. يسري على الشروط القانون الإسرائيلي، والاختصاص للمحاكم المختصة في إسرائيل.'] },
        { h: 'التواصل', p: [`أسئلة حول الشروط: ${E}`] },
      ],
    },
    privacy: {
      title: 'سياسة الخصوصية',
      intro: 'توضح هذه الصفحة ما هي المعلومات التي تجمعها EsayWeek، ولماذا، ومع من تُشارك، وما هي حقوقك.',
      sections: [
        { h: 'من المسؤول عن المعلومات', p: ['EsayWeek مسؤولة عن بيانات أصحاب الأعمال وعن الموقع.', 'العمل نفسه مسؤول عن بيانات زبائنه (المواعيد والرسائل والصور)، وEsayWeek تحفظها وتعالجها نيابةً عنه. يمكن توجيه الطلبات بشأنها إلى العمل أو إلينا.'] },
        { h: 'ما هي المعلومات التي نجمعها', p: ['أصحاب الأعمال: الاسم، البريد الإلكتروني، الهاتف، تفاصيل العمل، الخدمات والأسعار، ساعات العمل، وكلمة المرور (محفوظة مشفّرة).', 'الزبائن: الاسم، رقم الهاتف، المواعيد والملاحظات، الصور المرفوعة للموعد، التقييمات، والرسائل والملفات المرسلة إلى روبوت المحادثة الخاص بالعمل في واتساب.', 'معلومات تقنية: عنوان IP وعمليات الدخول والتعديل (للأمان ومنع إساءة الاستخدام).'] },
        { h: 'لماذا', p: ['لحجز المواعيد وإدارتها، وإرسال التأكيدات والتذكيرات وإشعارات التغيير، وتشغيل روبوت المحادثة، وتأمين النظام وتقديم الدعم. لا نبيع المعلومات ولا نستخدمها للإعلانات.'] },
        { h: 'مع من نشارك المعلومات', p: ['مع العمل الذي حجزت عنده.', 'مع مزوّدين يشغّلون الخدمة لصالحنا: الاستضافة السحابية (Railway، خوادم قد تكون خارج إسرائيل)، إرسال البريد (Brevo، Google)، WhatsApp لنقل الرسائل، مزوّد الدفع عند تفعيل الدفع بالبطاقة، وإن فُعّل، مزوّد ذكاء اصطناعي (OpenAI) لفهم رسائل المحادثة.', 'وعندما يفرض القانون ذلك.'] },
        { h: 'مدة الحفظ', p: ['تُحذف الملفات المرسلة في المحادثة بعد 90 يومًا. تُحفظ بقية المعلومات ما دام حساب العمل نشطًا، أو كما يفرض القانون.'] },
        { h: 'التخزين في المتصفح', p: ['يحفظ الموقع في متصفحك بيانات تسجيل الدخول واللغة والمظهر الذي اخترته. لا توجد ملفات تعريف ارتباط للإعلانات أو التتبع.'] },
        { h: 'الأمان', p: ['الاتصال مشفّر (HTTPS)، وكلمات المرور محفوظة كتجزئة، والوصول إلى البيانات محدود. لا يوجد نظام محصّن تمامًا، وإذا وقع حادث أمني يخصّك فسنبلغك كما يفرض القانون.'] },
        { h: 'حقوقك', p: ['وفق قانون حماية الخصوصية، 1981، يمكنك طلب الاطلاع على المعلومات المتعلقة بك أو تصحيحها أو حذفها، وطلب التوقف عن تلقي الرسائل. للطلبات: ' + E + '.'] },
        { h: 'القاصرون', p: ['لا يُسمح بفتح حساب عمل لمن هم دون 18 عامًا.'] },
        { h: 'التغييرات', p: ['سنحدّث هذه الصفحة عند حدوث تغيير. تاريخ آخر تحديث يظهر في أعلاها.'] },
      ],
    },
    accessibility: {
      title: 'بيان إمكانية الوصول',
      intro: 'نريد أن يتمكن الجميع من حجز موعد وإدارة عمل في EsayWeek، بما في ذلك الأشخاص ذوو الإعاقة.',
      sections: [
        { h: 'مستوى إمكانية الوصول', p: ['هدفنا التوافق مع المعيار الإسرائيلي 5568 وإرشادات WCAG 2.0 بمستوى AA. لم يخضع الموقع بعد لفحص كامل من مختص إمكانية وصول معتمد، لذلك قد لا تكون بعض أجزائه متاحة بالكامل.'] },
        { h: 'ما هو متوفر', p: ['دعم العبرية والعربية والإنجليزية، بما في ذلك اتجاه الكتابة من اليمين إلى اليسار.', 'وضع داكن ووضع فاتح.', 'ملاءمة لشاشات الهاتف ولتكبير النص في المتصفح.', 'تظهر رسائل الخطأ كنص.'] },
        { h: 'قيود معروفة', p: ['بعض الأزرار تعتمد على أيقونة فقط، وبعض الحقول لها نص توضيحي داخلي فقط، ولم يُفحص التقويم وجدول صاحب العمل بعد مع قارئ الشاشة. نعمل على التحسين.'] },
        { h: 'واجهت مشكلة؟', p: [`يسعدنا أن نسمع ونساعد، بما في ذلك حجز موعد بطريقة أخرى. منسّق إمكانية الوصول: ${E}. نحاول الرد خلال 5 أيام عمل.`] },
      ],
    },
  },
  EN: {
    terms: {
      title: 'Terms of Use',
      intro: 'These terms apply to every use of the EsayWeek website and service ("the Service"), by business owners and by customers who book appointments. Using the Service means you accept them.',
      sections: [
        { h: 'What the Service does', p: ['EsayWeek is a booking system for service businesses: a business page, online booking, reminders, a waitlist and a WhatsApp chatbot.', 'An appointment is an arrangement between the customer and the business. EsayWeek is not a party to the service the business provides, its price or its quality.'] },
        { h: 'Business owner accounts', p: ['A business account is opened after its request is approved. The owner is responsible for the accuracy of their details, for keeping the password safe, and for everything done in the account.', 'The owner is responsible for having permission to keep their customers\' details and message them, and for using the Service lawfully, including privacy and messaging laws.'] },
        { h: 'Payment', p: ['The Service is a monthly subscription, after a trial period if one was offered. The price and payment method are set at sign-up or agreed with us. Non-payment may lead to the account being paused.'] },
        { h: 'WhatsApp connection', p: ['The chatbot runs through a WhatsApp number the owner links themselves (like WhatsApp Web), not through WhatsApp\'s official business platform. WhatsApp may restrict or ban a number linked this way. The owner understands and accepts this risk, and EsayWeek is not liable for a ban or lost access to the number.'] },
        { h: 'Prohibited use', p: ['Do not use the Service for spam, abusive or illegal content, impersonation, attacks on its security, or access to other people\'s data.'] },
        { h: 'Availability and liability', p: ['The Service is provided as is. We work to keep it running, but we do not promise it will be free of faults, outages or lost messages.', 'To the extent the law allows, EsayWeek\'s liability is limited to the amount paid for the Service in the three months before the event, and we are not liable for indirect damage, lost profit or a missed appointment.'] },
        { h: 'Ending', p: ['An owner can stop using the Service at any time. We may pause or close an account that breaks these terms or is unpaid.'] },
        { h: 'Changes and law', p: ['We may update these terms; the last update date is shown at the top. Israeli law governs them, and the competent courts in Israel have jurisdiction.'] },
        { h: 'Contact', p: [`Questions about these terms: ${E}`] },
      ],
    },
    privacy: {
      title: 'Privacy Policy',
      intro: 'This page explains what information EsayWeek collects, why, who it is shared with, and your rights.',
      sections: [
        { h: 'Who is responsible', p: ['EsayWeek is responsible for business owners\' details and for the website.', 'Each business is responsible for its own customers\' details (appointments, messages, photos); EsayWeek stores and processes them on its behalf. Requests about that information can go to the business or to us.'] },
        { h: 'What we collect', p: ['Business owners: name, email, phone, business details, services and prices, opening hours, and a password (stored hashed).', 'Customers: name, phone number, appointments and notes, photos uploaded for an appointment, reviews, and messages and files sent to the business\'s WhatsApp chatbot.', 'Technical: IP address and sign-in and change events (for security and abuse prevention).'] },
        { h: 'Why', p: ['To book and manage appointments, send confirmations, reminders and change notices, run the chatbot, secure the system and give support. We do not sell information or use it for advertising.'] },
        { h: 'Who we share it with', p: ['The business you booked with.', 'Providers that run the Service for us: cloud hosting (Railway, servers that may be outside Israel), email delivery (Brevo, Google), WhatsApp to carry messages, a payment provider once card payments are enabled, and, if enabled, an AI provider (OpenAI) to understand chat messages.', 'And where the law requires it.'] },
        { h: 'How long we keep it', p: ['Files sent in chat are deleted after 90 days. Other information is kept while the business\'s account is active, or as the law requires.'] },
        { h: 'Browser storage', p: ['The site stores your sign-in, language and theme in your browser. There are no advertising or tracking cookies.'] },
        { h: 'Security', p: ['Connections are encrypted (HTTPS), passwords are stored hashed, and access to data is limited. No system is perfectly secure; if a security incident affects you, we will notify you as the law requires.'] },
        { h: 'Your rights', p: ['Under Israel\'s Protection of Privacy Law, 1981, you can ask to see, correct or delete information about you, and ask to stop receiving messages. Requests: ' + E + '.'] },
        { h: 'Minors', p: ['Business accounts are not for people under 18.'] },
        { h: 'Changes', p: ['We will update this page when something changes; the last update date is shown at the top.'] },
      ],
    },
    accessibility: {
      title: 'Accessibility Statement',
      intro: 'We want everyone to be able to book and run a business on EsayWeek, including people with disabilities.',
      sections: [
        { h: 'Accessibility level', p: ['Our target is Israeli Standard 5568 and WCAG 2.0 level AA. The site has not yet had a full audit by a certified accessibility expert, so some parts may not be fully accessible.'] },
        { h: 'What is in place', p: ['Hebrew, Arabic and English, including right-to-left layout.', 'Dark and light modes.', 'Layouts that fit phone screens and browser text zoom.', 'Error messages are shown as text.'] },
        { h: 'Known limitations', p: ['Some buttons are icon-only, some form fields have only placeholder text, and the owner\'s calendar and schedule have not yet been tested with a screen reader. We are working on it.'] },
        { h: 'Found a problem?', p: [`Tell us and we will help, including booking another way. Accessibility coordinator: ${E}. We aim to reply within 5 business days.`] },
      ],
    },
  },
}

export function legalText(lang: string, doc: LegalDoc): DocText {
  const l: Lang = lang === 'AR' || lang === 'EN' ? lang : 'HE'
  return content[l][doc]
}
