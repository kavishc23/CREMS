const databaseName = 'crems-rental-drafts'
const storeName = 'workflows'

function database(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(databaseName, 1)
    request.onupgradeneeded = () => request.result.createObjectStore(storeName)
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error)
  })
}

async function access<T>(key: string, mode: IDBTransactionMode, operation: (store: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  const db = await database()
  try {
    return await new Promise<T>((resolve, reject) => {
      const transaction = db.transaction(storeName, mode)
      const request = operation(transaction.objectStore(storeName))
      transaction.oncomplete = () => resolve(request.result)
      transaction.onerror = () => reject(transaction.error)
      transaction.onabort = () => reject(transaction.error)
    })
  } finally { db.close() }
}

export async function readRentalDraft<T>(key: string): Promise<T | undefined> {
  const saved = await access<T | undefined>(key, 'readonly', store => store.get(key))
  if (saved !== undefined || !key.startsWith('pickup:')) return saved
  const legacyKey = key.replace('pickup:', 'pickup-draft:')
  const legacy = sessionStorage.getItem(legacyKey)
  if (!legacy) return undefined
  try {
    const draft = JSON.parse(legacy) as T
    await saveRentalDraft(key, draft)
    sessionStorage.removeItem(legacyKey)
    return draft
  } catch { return undefined }
}

export async function saveRentalDraft<T>(key: string, value: T): Promise<void> {
  await access(key, 'readwrite', store => store.put(value, key))
}

export async function clearRentalDraft(key: string): Promise<void> {
  await access(key, 'readwrite', store => store.delete(key))
}
