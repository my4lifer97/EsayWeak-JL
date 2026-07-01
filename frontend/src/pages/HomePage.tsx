import { Link } from 'react-router-dom'

const FEATURES = [
  { icon: '📅', title: 'Smart Scheduling', desc: 'Set working hours, breaks, and blocked dates. Available slots update automatically.' },
  { icon: '💬', title: 'WhatsApp Reminders', desc: 'Customers receive automated reminders 24 hours before their appointment.' },
  { icon: '🤖', title: 'WhatsApp Chatbot', desc: 'Customers can book, cancel, or reschedule directly via WhatsApp message.' },
  { icon: '🌍', title: '3 Languages', desc: 'Full support for English, Arabic (RTL), and Hebrew (RTL).' },
  { icon: '📱', title: 'Mobile-Friendly', desc: 'Beautiful booking page that works perfectly on any device.' },
  { icon: '✂️', title: 'Service Management', desc: 'Add your services with names in all supported languages, duration, and price.' },
]

export default function HomePage() {
  return (
    <div className="min-h-screen bg-gray-950 text-white">
      <nav className="flex items-center justify-between px-6 py-4 border-b border-gray-900 max-w-6xl mx-auto">
        <div className="font-bold text-xl">✂️ BarberBook</div>
        <div className="flex gap-4">
          <Link to="/admin/login" className="text-gray-400 hover:text-white text-sm transition-colors">Sign In</Link>
          <Link to="/admin/register" className="bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium px-4 py-2 rounded-lg transition-colors">Get Started Free</Link>
        </div>
      </nav>

      <section className="text-center py-24 px-4 max-w-3xl mx-auto">
        <div className="inline-block bg-blue-600/10 border border-blue-600/30 text-blue-400 text-sm font-medium px-4 py-1.5 rounded-full mb-6">
          30-day free trial · No credit card required
        </div>
        <h1 className="text-5xl font-bold leading-tight mb-6">Smart booking system<br /><span className="text-blue-400">for modern barbers</span></h1>
        <p className="text-gray-400 text-xl mb-10 leading-relaxed">Let customers book online and receive WhatsApp reminders automatically. You focus on cutting hair — we handle the scheduling.</p>
        <div className="flex gap-4 justify-center flex-wrap">
          <Link to="/admin/register" className="bg-blue-600 hover:bg-blue-700 text-white font-bold px-8 py-4 rounded-2xl text-lg transition-colors">Start Free Trial</Link>
          <Link to="/admin/login" className="bg-gray-800 hover:bg-gray-700 text-white font-semibold px-8 py-4 rounded-2xl text-lg transition-colors">Sign In</Link>
        </div>
      </section>

      <section className="py-20 px-4 max-w-5xl mx-auto">
        <h2 className="text-3xl font-bold text-center mb-12">Everything you need</h2>
        <div className="grid md:grid-cols-3 gap-6">
          {FEATURES.map((f) => (
            <div key={f.title} className="bg-gray-900 border border-gray-800 rounded-2xl p-6">
              <div className="text-3xl mb-4">{f.icon}</div>
              <h3 className="font-semibold text-lg mb-2">{f.title}</h3>
              <p className="text-gray-400 text-sm leading-relaxed">{f.desc}</p>
            </div>
          ))}
        </div>
      </section>

      <section className="py-20 px-4 max-w-md mx-auto text-center">
        <h2 className="text-3xl font-bold mb-4">Simple pricing</h2>
        <p className="text-gray-400 mb-10">One plan, everything included.</p>
        <div className="bg-gray-900 border border-blue-600/40 rounded-2xl p-8">
          <div className="text-5xl font-bold mb-2">$20<span className="text-xl text-gray-400 font-normal">/month</span></div>
          <p className="text-gray-400 mb-8">per barber</p>
          <ul className="text-start space-y-3 mb-8">
            {['Unlimited appointments','WhatsApp reminders','WhatsApp chatbot','English, Arabic & Hebrew','Online booking page','Admin dashboard','30-day free trial'].map((item) => (
              <li key={item} className="flex items-center gap-3 text-sm">
                <span className="text-green-400">✓</span>
                <span className="text-gray-300">{item}</span>
              </li>
            ))}
          </ul>
          <Link to="/admin/register" className="block w-full bg-blue-600 hover:bg-blue-700 text-white font-bold py-3.5 rounded-xl transition-colors">
            Start 30-Day Free Trial
          </Link>
        </div>
      </section>

      <footer className="border-t border-gray-900 py-8 text-center text-gray-600 text-sm">
        © 2026 BarberBook. Built for barbers.
      </footer>
    </div>
  )
}
