namespace Estacionamento.Domain.Parking;

public sealed record ParkingSpace
{
    private ParkingSpace(Guid id, SpaceCode code)
    {
        Id = id;
        Code = code;
    }

    public Guid Id { get; }
    public SpaceCode Code { get; }

    public static ParkingSpace Create(string? code) => new(Guid.NewGuid(), SpaceCode.Create(code));
}
