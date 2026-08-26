import axios from 'axios'

// See api.ts's comment: frontend and backend deploy as separate services with separate
// URLs in production, so the relative '/api' proxy that works in local dev doesn't exist
// in the built static output.
export const customerApi = axios.create({ baseURL: `${import.meta.env.VITE_API_URL ?? ''}/api` })

customerApi.interceptors.request.use((config) => {
  const token = localStorage.getItem('customerToken')
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

customerApi.interceptors.response.use(
  (r) => r,
  (err) => {
    if (err.response?.status === 401) {
      localStorage.removeItem('customerToken')
      localStorage.removeItem('customerUser')
    }
    return Promise.reject(err)
  }
)
