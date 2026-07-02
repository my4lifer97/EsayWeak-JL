import axios from 'axios'

export const customerApi = axios.create({ baseURL: '/api' })

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
