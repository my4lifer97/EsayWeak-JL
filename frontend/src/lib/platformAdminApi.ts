import axios from 'axios'

// See api.ts's comment: frontend and backend deploy as separate services with separate
// URLs in production, so the relative '/api' proxy that works in local dev doesn't exist
// in the built static output.
export const platformAdminApi = axios.create({ baseURL: `${import.meta.env.VITE_API_URL ?? ''}/api` })

platformAdminApi.interceptors.request.use((config) => {
  const token = localStorage.getItem('platformAdminToken')
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

platformAdminApi.interceptors.response.use(
  (r) => r,
  (err) => {
    if (err.response?.status === 401) {
      localStorage.removeItem('platformAdminToken')
      localStorage.removeItem('platformAdminUser')
    }
    return Promise.reject(err)
  }
)
