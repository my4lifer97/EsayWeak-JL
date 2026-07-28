import axios from 'axios'

export const platformAdminApi = axios.create({ baseURL: '/api' })

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
