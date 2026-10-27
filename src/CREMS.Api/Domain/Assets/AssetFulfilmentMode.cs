namespace CREMS.Api.Domain.Assets;

public enum AssetFulfilmentMode { PickupOrDelivery, PickupOnly, DeliveryOnly }

public static class PublicRentalRequestPolicy
{
    public static AssetFulfilmentMode Fulfilment(Asset asset) => asset.Type == AssetType.PropertyUnit
        ? AssetFulfilmentMode.PickupOnly : asset.RequiresDelivery ? AssetFulfilmentMode.DeliveryOnly : asset.FulfilmentMode;
    public static bool RequiresQuotation(Asset asset, bool delivery, bool personnel) =>
        delivery || personnel;
}
