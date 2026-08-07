window.drawTrendChart = function (data) {
    const canvas = document.getElementById('trend-chart');
    if (!canvas) return;

    // Destroy existing chart if any
    const existing = Chart.getChart(canvas);
    if (existing) existing.destroy();

    new Chart(canvas, {
        type: 'bar',
        data: {
            labels: data.labels,
            datasets: [
                {
                    label: '完成',
                    data: data.ok,
                    backgroundColor: '#10B981',
                    borderRadius: 4,
                    barPercentage: 0.7,
                },
                {
                    label: '正在进行中',
                    data: data.ng,
                    backgroundColor: '#F59E0B',
                    borderRadius: 4,
                    barPercentage: 0.7,
                },
                {
                    label: '待测 / 待确认',
                    data: data.pending,
                    backgroundColor: '#94A3B8',
                    borderRadius: 4,
                    barPercentage: 0.7,
                },
                {
                    label: '未填写',
                    data: data.empty,
                    backgroundColor: '#E2E8F0',
                    borderRadius: 4,
                    barPercentage: 0.7,
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    position: 'bottom',
                    labels: {
                        usePointStyle: true,
                        padding: 20,
                        font: { size: 12 }
                    }
                }
            },
            scales: {
                x: {
                    stacked: true,
                    grid: { display: false },
                    ticks: { font: { size: 11 } }
                },
                y: {
                    stacked: true,
                    beginAtZero: true,
                    ticks: {
                        stepSize: 1,
                        font: { size: 11 }
                    },
                    grid: { color: '#F1F5F9' }
                }
            }
        }
    });
};
