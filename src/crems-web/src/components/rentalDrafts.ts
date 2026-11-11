const databaseName = 'crems-rental-drafts'
const storeName = 'workflows'
const pendingWrites = new Map<string, Promise<void>>()

// Keep an older autosave from overwriting a manual save or resurrecting a discarded draft.
function write(key: string, operation: () => Promise<void>): Promise<void> {
  const pending = (pendingWrites.get(key) ?? Promise.resolve()).catch(() => {}).then(operation)
  pendingWrites.set(key, pending)
  void pending.finally(() => { if (pendingWrites.get(key) === pending) pendingWrites.delete(key) }).catch(() => {})
  return pending
}

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
  await pendingWrites.get(key)?.catch(() => {})
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
  // Capture this revision before another render can update the form.
  const snapshot = structuredClone(value)
  await write(key, async () => { await access(key, 'readwrite', store => store.put(snapshot, key)) })
}

export async function clearRentalDraft(key: string): Promise<void> {
  await write(key, async () => { await access(key, 'readwrite', store => store.delete(key)) })
}
