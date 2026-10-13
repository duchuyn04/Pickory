import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { AdminFormDialog } from '../../../../shared/admin-form-dialog';
import { ErrorAlert } from '../../../../shared/error-alert';
import {
  MY_ADDRESSES_KEY,
  SAVED_ADDRESS_LIMIT,
  deleteSavedAddress,
  describeAddress,
  updateSavedAddress,
  useSavedAddresses,
  type SavedAddress,
} from './addresses-api';
import { AddressFormDialog } from './address-form-dialog';
import './address-book.css';

type OpenDialog =
  { kind: 'form'; editing: SavedAddress | null } | { kind: 'delete'; address: SavedAddress };

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}

// Thẻ sổ địa chỉ trên trang tài khoản. Trang chỉ ghép component, mọi logic nằm ở đây.
export function AddressBook() {
  const queryClient = useQueryClient();
  const addressesQuery = useSavedAddresses(true);
  const [openDialog, setOpenDialog] = useState<OpenDialog | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [isWorking, setIsWorking] = useState(false);

  const addresses = addressesQuery.data ?? [];
  const isFull = addresses.length >= SAVED_ADDRESS_LIMIT;

  async function handleSetDefault(address: SavedAddress) {
    // Bấm lần hai trong lúc chờ API không được gửi thêm request.
    if (isWorking) return;
    setActionError(null);
    setIsWorking(true);
    try {
      await updateSavedAddress(address.id, { isDefault: true });
      await queryClient.invalidateQueries({ queryKey: MY_ADDRESSES_KEY });
    } catch (error: unknown) {
      setActionError(errorMessage(error, 'Đặt địa chỉ mặc định thất bại.'));
    } finally {
      setIsWorking(false);
    }
  }

  async function handleDelete(address: SavedAddress) {
    if (isWorking) return;
    setDeleteError(null);
    setIsWorking(true);
    try {
      await deleteSavedAddress(address.id);
      // Xóa xong thì tải lại sổ. Xóa địa chỉ mặc định không tự chọn địa chỉ khác.
      await queryClient.invalidateQueries({ queryKey: MY_ADDRESSES_KEY });
      setOpenDialog(null);
    } catch (error: unknown) {
      // Giữ hộp thoại mở để khách đọc lý do.
      setDeleteError(errorMessage(error, 'Xóa địa chỉ thất bại.'));
    } finally {
      setIsWorking(false);
    }
  }

  return (
    <section className="card account-section" aria-labelledby="address-book-title">
      <div className="address-book-head">
        <h2 id="address-book-title">Sổ địa chỉ</h2>
        <button
          type="button"
          className="btn btn-primary btn-sm"
          disabled={isFull}
          onClick={() => setOpenDialog({ kind: 'form', editing: null })}
        >
          Thêm địa chỉ
        </button>
      </div>

      {isFull && (
        <p className="muted small">
          Đã đủ {SAVED_ADDRESS_LIMIT} địa chỉ. Xóa bớt một địa chỉ trước khi thêm địa chỉ mới.
        </p>
      )}

      {actionError && <ErrorAlert message={actionError} />}

      {addressesQuery.isPending ? (
        <p role="status" className="muted">
          Đang nạp sổ địa chỉ...
        </p>
      ) : addressesQuery.isError ? (
        <ErrorAlert
          message={
            addressesQuery.error instanceof Error
              ? addressesQuery.error.message
              : 'Tải sổ địa chỉ thất bại.'
          }
        />
      ) : addresses.length === 0 ? (
        <p className="muted">Bạn chưa lưu địa chỉ nào. Thêm địa chỉ để đặt hàng nhanh hơn.</p>
      ) : (
        <ul className="address-book-list">
          {addresses.map((address) => (
            <li key={address.id} className="address-book-item">
              <div className="address-book-body">
                <div className="address-book-title">
                  <strong>{address.label}</strong>
                  {address.isDefault && <span className="badge badge-ok">Mặc định</span>}
                </div>
                <p>
                  {address.name}, {address.phone}
                </p>
                <p className="muted wrap-anywhere">{describeAddress(address.address)}</p>
              </div>
              <div className="row-actions">
                {!address.isDefault && (
                  <button
                    type="button"
                    className="btn btn-sm"
                    disabled={isWorking}
                    onClick={() => void handleSetDefault(address)}
                  >
                    Đặt làm mặc định
                  </button>
                )}
                <button
                  type="button"
                  className="btn btn-sm"
                  onClick={() => setOpenDialog({ kind: 'form', editing: address })}
                >
                  Sửa
                </button>
                <button
                  type="button"
                  className="btn btn-sm btn-danger"
                  onClick={() => {
                    setDeleteError(null);
                    setOpenDialog({ kind: 'delete', address });
                  }}
                >
                  Xóa
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}

      {openDialog?.kind === 'form' && (
        <AddressFormDialog
          editing={openDialog.editing}
          isFirstAddress={addresses.length === 0}
          onClose={() => setOpenDialog(null)}
        />
      )}

      {openDialog?.kind === 'delete' && (
        <AdminFormDialog
          isOpen
          onClose={() => setOpenDialog(null)}
          title="Xóa địa chỉ"
          formId="address-delete-form"
          onSubmit={(event) => {
            event.preventDefault();
            void handleDelete(openDialog.address);
          }}
          isSubmitting={isWorking}
          submitText="Xóa địa chỉ"
          error={deleteError}
        >
          <p>
            Xóa địa chỉ <strong>{openDialog.address.label}</strong> khỏi sổ? Đơn đã đặt không bị ảnh
            hưởng.
          </p>
        </AdminFormDialog>
      )}
    </section>
  );
}
